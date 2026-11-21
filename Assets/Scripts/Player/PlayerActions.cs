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

        // sub balls sit on a ring around the main ball
        public float SlotAngleFor(int index)
        {
            int slots = tuning.maxBallCount - 1;
            return 90f + 360f / slots * (index - 1);
        }

        public bool TrySplit(Vector2 facing, out SizeController source, out SizeController newBall, out float newMass)
        {
            source = null;
            newBall = null;
            newMass = 0f;

            if (Time.time < nextSplitTime)
            {
                return false;
            }

            source = roster.Largest();

            if (source == null)
            {
                return false;
            }

            if (source.Mass <= tuning.splitMinMass)
            {
                Debug.Log($"[split] mass {source.Mass:F0} is not above {tuning.splitMinMass:F0}");
                return false;
            }

            if (roster.Count >= tuning.maxBallCount)
            {
                Debug.Log($"[split] ball limit {tuning.maxBallCount} reached");
                return false;
            }

            SpriteRenderer shell = source.GetComponent<SpriteRenderer>();

            if (shell == null)
            {
                Debug.LogError("[split] source ball has no SpriteRenderer");
                return false;
            }

            Vector2 dir = Direction(facing);
            newMass = source.Mass * tuning.splitMassRatio;  //*0.5 half

            // halve first, so the new ball spawns outside the smaller body
            source.SetMass(source.Mass - newMass);

            Vector3 position = source.transform.position + (Vector3)(dir * (source.Radius * 2f + tuning.splitSpawnGap));
            //($"{ballPrefix}_Ball{roster.Count}" ball name
            newBall = spawnBall($"{ballPrefix}_Ball{roster.Count}", position, newMass, shell.sprite, shell.color);

            if (newBall == null)
            {
                source.SetMass(source.Mass + newMass);   // put the mass back, nothing happened
                return false;
            }

            Rigidbody2D body = newBall.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.linearVelocity = dir * tuning.splitLaunchSpeed;
            }

            nextSplitTime = Time.time + tuning.splitCooldown;
            return true;
        }

        // true when food  spat out; source is the ball that paid for it
        public bool TrySpit(Vector2 facing, out SizeController source)
        {
            source = null;

            if (Time.time < nextSpitTime) return false;

            source = roster.Largest();

            if (source == null)
            {
                return false;
            }
                //check Mass enought? 
            if (!source.TryConsumeMass(tuning.spitMassCost, tuning.minMassAfterSpit))
            {
                Debug.Log($"[spit] mass {source.Mass:F0} is not above {tuning.spitMassCost + tuning.minMassAfterSpit:F0}");
                return false;
            }

            SpriteRenderer shell = source.GetComponent<SpriteRenderer>();
            Sprite sprite = shell != null ? shell.sprite : null;
            Color color = shell != null ? shell.color : Color.cyan;

            Vector2 dir = Direction(facing);
            Vector3 position = source.transform.position + (Vector3)(dir * (source.Radius + tuning.spitSpawnGap));

            BallFactory.CreateSpitFood("SpitFood", position, dir * tuning.spitLaunchSpeed,
                tuning.spitFoodMass, sprite, Lighten(color), tuning.spitArmDelay);

            nextSpitTime = Time.time + tuning.spitCooldown;
            return true;
        }

        private static Vector2 Direction(Vector2 facing)
        {
            return facing.sqrMagnitude > 0.0001f ? facing : Vector2.right;
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
