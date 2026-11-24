using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // split and spit rules: cooldowns, mass math, where the new ball appears.
    public class PlayerActions
    {
        private PlayerRoster roster;
        //Generic Delegate (get black SizeController)
        private System.Func<string, Vector3, float, Sprite, Color, SizeController> spawnBall;
        private PlayerController.PlayerTuning tuning;
        private string ballPrefix = "Player";

        private float nextSplitTime;
        private float nextSpitTime;

        public void Configure(PlayerRoster roster,System.Func<string, Vector3, float, Sprite, Color, SizeController> spawnBall,
            PlayerController.PlayerTuning tuning, string ballPrefix)
        {
            this.roster = roster;
            this.spawnBall = spawnBall;
            this.tuning = tuning;
            this.ballPrefix = ballPrefix;
        }

        // Agar.io split: every cell that is big enough splits in two at once, not just the
        // biggest one. Once the player is past half of the cell limit only the biggest cells
        // split, and only as many as the limit still allows.
        public List<SizeController> TrySplit(Vector2 aim, float centerAmount)
        {
            if (Time.time < nextSplitTime)
            {
                return null;
            }

            if (roster == null || roster.Count == 0)
            {
                return null;
            }

            int room = tuning.maxBallCount - roster.Count;
            if (room <= 0)
            {
                Debug.Log($"[split] ball limit {tuning.maxBallCount} reached");
                return null;
            }

            List<SizeController> candidates = new List<SizeController>();
            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }
                if (ball.Mass <= tuning.splitMinMass)
                {
                    continue;   // too small to split
                }
                candidates.Add(ball);
            }

            if (candidates.Count == 0)
            {
                Debug.Log($"[split] no ball is above {tuning.splitMinMass:F0} mass");
                return null;
            }

            candidates.Sort((a, b) => b.Mass.CompareTo(a.Mass));

            int allowed = roster.Count > tuning.maxBallCount / 2
                ? Mathf.Max(0, tuning.maxBallCount - roster.Count + 1)   // only the big ones
                : candidates.Count;                                      // everyone splits

            allowed = Mathf.Min(allowed, Mathf.Min(candidates.Count, room));
            if (allowed <= 0)
            {
                return null;
            }

            Vector2 dir = Direction(aim);
            Vector2 centroid = Centroid();
            List<SizeController> touched = new List<SizeController>(allowed * 2);

            for (int i = 0; i < allowed; i++)
            {
                SizeController source = candidates[i];
                if (source == null || source.IsDead)
                {
                    continue;
                }

                SpriteRenderer shell = source.GetComponent<SpriteRenderer>();
                if (shell == null)
                {
                    continue;
                }

                float newMass = source.Mass * tuning.splitMassRatio;   // half, not a copy
                if (newMass <= 0f)
                {
                    continue;
                }

                // halve first, so the parent is already the smaller size while the piece
                source.SetMass(source.Mass - newMass);

                // the piece is born in the middle of the body; its motion component then slides
                // it out while it grows, so you can watch it leave the parent.
                // The distance comes from both radii, not from a fixed world gap: a big cell
                // throws its piece farther than a small one.
                Vector3 origin = source.transform.position;
                float splitRadius = source.Radius * Mathf.Sqrt(tuning.splitMassRatio);
                float distance = (source.Radius + splitRadius) * tuning.splitDistanceMultiplier;
                Vector2 cellDir = CellDirection(dir, centroid, origin, roster.Count, centerAmount);
                Vector3 target = origin + (Vector3)(cellDir * distance);
                SizeController newBall = spawnBall($"{ballPrefix}_Ball{roster.Count}", origin, newMass, shell.sprite, shell.color);

                if (newBall == null)
                {
                    source.SetMass(source.Mass + newMass);   // put the mass back, nothing happened
                    continue;
                }

                PlayerCellMotion motion = newBall.GetComponent<PlayerCellMotion>();
                if (motion != null)
                {
                    // impulse scales with the group's own speed, so a heavy group does not get
                    // the same absolute kick as a light one
                    float groupSpeed = tuning.moveSpeed *
                        (roster.Main != null ? roster.Main.MoveSpeedFactor : 1f);
                    float impulse = groupSpeed * tuning.splitImpulseFactor * GamePacing.SplitLaunch;

                    motion.BeginSplit(origin, target, tuning.splitEjectDuration, impulse, tuning.splitCoastDuration);
                }

                touched.Add(source);
                touched.Add(newBall);
            }

            if (touched.Count == 0)
            {
                return null;
            }

            nextSplitTime = Time.time + tuning.splitCooldown;
            return touched;
        }

        // Agar ejects from EVERY cell at once: 16 pieces means 16 pellets, one per body part.
        // Returns how many pieces were spat out (0 = nothing happened).
        public int TrySpit(Vector2 aim, float centerAmount)
        {
            if (Time.time < nextSpitTime) return 0;

            if (roster == null || roster.Count == 0) return 0;

            Vector2 dir = Direction(aim);
            Vector2 centroid = Centroid();
            int ejected = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead || ball.IsEjecting)
                {
                    continue;
                }

                // each cell pays for its own pellet, so a thin cell simply can not spit
                if (!ball.TryConsumeMass(tuning.spitMassCost, tuning.minMassAfterSpit))
                {
                    continue;
                }

                SpriteRenderer shell = ball.GetComponent<SpriteRenderer>();
                Sprite sprite = shell != null ? shell.sprite : null;
                Color color = shell != null ? shell.color : Color.cyan;

                // Spawn outside the body: body radius + the pellet's own radius + the gap.
                // Spawning at body radius only buries half of the pellet inside the ball, and
                // no arm delay can hide that.
                Vector2 cellDir = CellDirection(dir, centroid, ball.transform.position, roster.Count, centerAmount);

                // The pellet keeps its full size and full value for everyone else, but its own
                // spitter only gets part of the cost back, so spit-and-swallow can not print mass.
                float sporeMass = tuning.spitFoodMass;
                float payback = tuning.spitMassCost * tuning.spitReturnRatio;
                float sporeRadius = BallFactory.SporeRadius(sporeMass);
                float spawnDistance = ball.Radius + sporeRadius + tuning.spitSpawnGap;
                Vector3 position = ball.transform.position + (Vector3)(cellDir * spawnDistance);

                BallFactory.CreateSpitFood("SpitFood", position,
                    cellDir * (tuning.spitLaunchSpeed * GamePacing.SpitLaunch),
                    sporeMass, payback, sprite, Lighten(color), tuning.spitArmDelay, ball,
                    tuning.spitOwnerGrace);

                ejected++;
            }

            if (ejected == 0)
            {
                // nothing could spit: back off for a moment instead of re-testing every frame
                nextSpitTime = Time.time + Mathf.Max(0.05f, tuning.spitRetryInterval);
                return 0;
            }

            nextSpitTime = Time.time + tuning.spitCooldown;
            return ejected;
        }

        private static Vector2 Direction(Vector2 facing)
        {
            return facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector2.right;
        }

        // middle of the whole group: the centre spit aims every cell at this point
        private Vector2 Centroid()
        {
            if (roster == null || roster.Count == 0)
            {
                return Vector2.zero;
            }

            Vector2 sum = Vector2.zero;
            int n = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController ball = roster.Balls[i];
                if (ball == null || ball.IsDead)
                {
                    continue;
                }

                sum += (Vector2)ball.transform.position;
                n++;
            }

            return n > 0 ? sum / n : Vector2.zero;
        }

        // How each cell aims when it spits, driven by how far the stick is pushed in:
        //   stick pushed out  -> straight: every cell fires along the aim direction
        //   halfway           -> the streams start bending toward the middle and cross
        //   stick pushed in   -> centre spit: every cell fires at the middle of the group
        public static Vector2 CellDirection(Vector2 aim, Vector2 centroid, Vector2 cellPosition,
            int cellCount, float centerAmount)
        {
            Vector2 dir = Direction(aim);

            if (cellCount < 2)
            {
                return dir;
            }

            Vector2 offset = cellPosition - centroid;
            if (offset.sqrMagnitude < 0.0001f)
            {
                return dir;
            }

            // Sweep the angle instead of lerping vectors: two cells sitting side by side would
            // otherwise have their blend collapse to zero length and the pellet would snap.
            float fromDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Vector2 inward = (-offset).normalized;
            float toDeg = Mathf.Atan2(inward.y, inward.x) * Mathf.Rad2Deg;

            float sweep = Mathf.DeltaAngle(fromDeg, toDeg);
            float angle = (fromDeg + sweep * Mathf.Clamp01(centerAmount)) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        //make spit food more ealy to check and see
        private static Color Lighten(Color color)
        {
            return new Color(
                Mathf.Lerp(color.r, 1f, 0.35f),
                Mathf.Lerp(color.g, 1f, 0.35f),
                Mathf.Lerp(color.b, 1f, 0.35f),
                color.a);
        }
    }
}
