using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    public class PlayerMerge
    {
        private PlayerRoster roster;
        private PlayerController.PlayerTuning tuning;

        private readonly Dictionary<SizeController, float> readyAt = new Dictionary<SizeController, float>();

        private readonly List<Job> jobs = new List<Job>();

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
            if (merging.Contains(ball))
            {
                return false;
            }
            if (!readyAt.TryGetValue(ball, out float at))
            {
                return false;
            }
            return Time.time >= at;
        }

        public bool IsMerging(SizeController ball)
        {
            return ball != null && merging.Contains(ball);
        }

        public void Tick(float deltaTime)
        {
            PushApart(deltaTime);
            UpdateJobs();
            if (jobs.Count == 0)
            {
                Detect();
            }
        }

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

                    if (CanMerge(a) && CanMerge(b)) continue;

                    Vector2 offset = (Vector2)b.transform.position - (Vector2)a.transform.position;
                    float distance = offset.magnitude;
                    float overlap = a.Radius + b.Radius - distance;
                    if (overlap <= 0f) continue;

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

                    float contactDistance = a.Radius + b.Radius + tuning.mergeContactTolerance;
                    if (Vector2.Distance(a.transform.position, b.transform.position) > contactDistance) continue;

                    Begin(a, b);
                    return;
                }
            }
        }

        private void Begin(SizeController a, SizeController b)
        {
            SizeController keeper;
            SizeController small;

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
                return;
            }

            merging.Add(small);
            jobs.Add(new Job { small = small, keeper = keeper });

            motion.BeginMerge(keeper, tuning.mergeCrawlSpeed, tuning.mergeEatDepth, tuning.mergePullEase, () => Commit(small, keeper));
        }

        private void Commit(SizeController small, SizeController keeper)
        {
            jobs.RemoveAll(job => job.small == small);

            if (small == null || keeper == null || keeper.IsDead)
            {
                merging.Remove(small);
                return;
            }

            keeper.AddMass(small.Mass);

            small.Die(keeper);

            merging.Remove(small);
            readyAt.Remove(small);
        }

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

                if (!job.small.GetComponent<PlayerCellMotion>().IsMergingInto(job.keeper))
                {
                    merging.Remove(job.small);
                    jobs.RemoveAt(i);
                }
            }
        }
    }
}
