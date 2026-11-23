using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // Decides WHO may merge and WHEN, and nothing else.
    //
    // It used to also move the Rigidbody (MovePosition), change the visual scale and push
    // overlapping cells apart by writing body.position directly. That is now the job of
    // PlayerCellMotion: this class only says "these two may melt together" or "these two are
    // not allowed yet, separate them", and the cell itself does the moving.
    public class PlayerMerge
    {
        private PlayerRoster roster;
        private PlayerController.PlayerTuning tuning;

        // a ball may merge again at this time; 0 means "right away"
        private readonly Dictionary<SizeController, float> readyAt = new Dictionary<SizeController, float>();

        // balls that are being pulled in right now
        private readonly List<Job> jobs = new List<Job>();

        // used to tell "taken by an enemy" apart from "merged into our own ball"
        private readonly HashSet<SizeController> merging = new HashSet<SizeController>();

        private sealed class Job
        {
            public SizeController small;
            public SizeController keeper;
        }

        public void Configure(PlayerRoster roster, PlayerController.PlayerTuning tuning)
        {
            this.roster = roster;
            this.tuning = tuning;
        }

        public void Register(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }
            if (!readyAt.ContainsKey(ball))
            {
                readyAt[ball] = 0f;
            }
        }

        // a fresh split ball need wait cooldown before it can join again.
        // The wait is not a fixed number: a heavy cell has to wait longer, which is the
        // Agar.io idea (merge timer grows with the cell).
        public void MarkSplit(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }
            readyAt[ball] = Time.time + CooldownFor(ball.Mass);
        }

        private float CooldownFor(float mass)
        {
            float wait = tuning.mergeCooldown + Mathf.Max(0f, mass) * tuning.mergeCooldownPerMass;
            if (tuning.mergeCooldownMax > 0f)
            {
                wait = Mathf.Min(wait, tuning.mergeCooldownMax);
            }
            return Mathf.Max(0f, wait);
        }

        public void Drop(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }
            readyAt.Remove(ball);
            merging.Remove(ball);
            jobs.RemoveAll(job => job.small == ball);
        }

        public bool CanMerge(SizeController ball)
        {
            if (ball == null || ball.IsDead)
            {
                return false;
            }
            if (merging.Contains(ball)) //when  mergeing
            {
                return false;
            }
            if (!readyAt.TryGetValue(ball, out float at))      //not have Register
            {
                return false;
            }
            return Time.time >= at; //can
        }

        public bool IsMerging(SizeController ball)
        {
            return ball != null && merging.Contains(ball);
        }

        // finish the running pull first, then look for a new pair
        public void Tick(float deltaTime)
        {
            PushApart(deltaTime);   // cells are solid until the cooldown is over
            UpdateJobs();
            if (jobs.Count == 0)
            {
                Detect();
            }
        }

        // Agar.io makes the cells of one player solid bodies: while they are not allowed
        // to merge yet, two overlapping cells push each other out of the way instead of
        // passing through. This is pushAwayCollidingCells() from owenashurst/agar.io-clone
        // (src/server/map/player.js). The merge layer decides how far, the cell's motion
        // component performs the move, so the Rigidbody keeps a single writer.
        private void PushApart(float deltaTime)
        {
            if (roster == null || roster.Count < 2 || tuning == null || tuning.pushApartSpeed <= 0f || deltaTime <= 0f)
            {
                return;
            }

            float maxStep = tuning.pushApartSpeed * deltaTime;

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController a = roster.Balls[i];
                if (a == null || a.IsDead || a.IsEjecting || merging.Contains(a)) continue;

                PlayerCellMotion motionA = a.GetComponent<PlayerCellMotion>();
                if (motionA == null || motionA.IsBusy) continue;

                for (int j = i + 1; j < roster.Count; j++)
                {
                    SizeController b = roster.Balls[j];
                    if (b == null || b.IsDead || b.IsEjecting || merging.Contains(b)) continue;

                    PlayerCellMotion motionB = b.GetComponent<PlayerCellMotion>();
                    if (motionB == null || motionB.IsBusy) continue;

                    // both are allowed to melt together, so let them overlap on purpose
                    if (CanMerge(a) && CanMerge(b)) continue;

                    Vector2 offset = (Vector2)b.transform.position - (Vector2)a.transform.position;
                    float distance = offset.magnitude;
                    float overlap = a.Radius + b.Radius - distance;
                    if (overlap <= 0f) continue;

                    // two cells dropped on the exact same spot still have to separate
                    Vector2 direction = distance > 0.0001f ? offset / distance : Vector2.up;

                    float push = Mathf.Min(overlap * 0.5f, maxStep);
                    motionA.Nudge(-direction * push);
                    motionB.Nudge(direction * push);
                }
            }
        }

        private void Detect()
        {
            if (roster == null || roster.Count < 2)
            {
                return;
            }

            for (int i = 0; i < roster.Count; i++)
            {
                SizeController a = roster.Balls[i];
                if (!CanMerge(a)) continue;

                for (int j = i + 1; j < roster.Count; j++)
                {
                    SizeController b = roster.Balls[j];
                    if (!CanMerge(b)) continue;

                    //touch & time >true
                    float contactDistance = a.Radius + b.Radius + tuning.mergeContactTolerance;
                    if (Vector2.Distance(a.transform.position, b.transform.position) > contactDistance) continue;

                    Begin(a, b);
                    return;
                }
            }
        }

        private void Begin(SizeController a, SizeController b)
        {
            // the main ball always keeps itself, a sub ball may not eat it
            SizeController keeper;
            SizeController small;

            //find main ball
            if (a == roster.Main)
            {
                keeper = a;
                small = b;
            }
            else if (b == roster.Main)
            {
                keeper = b;
                small = a;
            }
            //see who big
            else if (a.Mass >= b.Mass)
            {
                keeper = a;
                small = b;
            }
            else
            {
                keeper = b;
                small = a;
            }

            PlayerCellMotion motion = small.GetComponent<PlayerCellMotion>();
            if (motion == null)
            {
                return;   // no motion owner means nothing can animate the swallow
            }

            merging.Add(small);
            jobs.Add(new Job { small = small, keeper = keeper });

            // the cell animates itself; when the pull is over it calls back so the mass is
            // committed in exactly one place
            motion.BeginMerge(keeper, tuning.mergePullDuration * GamePacing.MergeDuration, tuning.mergePullEase, () => Commit(small, keeper));
        }

        // called by the cell when its pull animation finished: this is the only place that
        // adds the mass, so a merge can never add it twice
        private void Commit(SizeController small, SizeController keeper)
        {
            jobs.RemoveAll(job => job.small == small);

            if (small == null || keeper == null || keeper.IsDead)
            {
                merging.Remove(small);
                return;
            }

            float gain = small.Mass;
            keeper.AddMass(gain);

            // Die fires the Eaten event while "merging" still holds the cell, so
            // PlayerController knows this was our own merge and not an enemy kill
            small.Die(keeper);

            merging.Remove(small);
            readyAt.Remove(small);
        }

        // the keeper may be eaten while the pull runs: give the cell back its body
        private void UpdateJobs()
        {
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                Job job = jobs[i];

                if (job.small == null || job.keeper == null || job.small.IsDead || job.keeper.IsDead)
                {
                    PlayerCellMotion motion = job.small != null ? job.small.GetComponent<PlayerCellMotion>() : null;
                    if (motion != null)
                    {
                        motion.AbortMerge();
                    }

                    if (job.small != null)
                    {
                        merging.Remove(job.small);
                    }

                    jobs.RemoveAt(i);
                    continue;
                }

                // finished animation removed itself from the list through Commit()
                if (!job.small.GetComponent<PlayerCellMotion>().IsMergingInto(job.keeper))
                {
                    merging.Remove(job.small);
                    jobs.RemoveAt(i);
                }
            }
        }
    }
}
