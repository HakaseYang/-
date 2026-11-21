using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
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
            public Vector2 startPosition;
            public Vector3 startScale;
            public float elapsed;   //time
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

        // a fresh split ball need wait cooldown before it can join again
        public void MarkSplit(SizeController ball)
        {
            if (ball == null)
            {
                return;
            }
                readyAt[ball] = Time.time + tuning.mergeCooldown;
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

        //  finish the running pull first, then look for a new pair
        public void Tick(float deltaTime)
        {
            UpdateJobs(deltaTime);
            if (jobs.Count == 0)
            {
                Detect();
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
                { keeper = a; small = b; }
            }
            else if (b == roster.Main)
            {
                { keeper = b; small = a; }
            }
            //see who big
            else if (a.Mass >= b.Mass)
            {
                { keeper = a; small = b; }
            }
            else
            {
                { keeper = b; small = a; }
            }

            // the small ball stops following  and starts the pull
            PlayerSubBall sub = small.GetComponent<PlayerSubBall>();
            if (sub != null)
            {
                sub.enabled = false;
            }   
            // colliders off while pulled
            Collider2D[] colliders = small.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;

            merging.Add(small);
            jobs.Add(new Job
            {
                small = small,
                keeper = keeper,
                startPosition = small.transform.position,
                startScale = small.transform.localScale,
                elapsed = 0f
            });

        }

        private void UpdateJobs(float deltaTime)
        {
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                Job job = jobs[i];

                // either side may be eaten mid-pull: give up instead of pushing
                if (job.small == null || job.keeper == null || job.small.IsDead || job.keeper.IsDead)
                {
                    Abort(job);
                    jobs.RemoveAt(i);
                    continue;
                }

                job.elapsed += deltaTime;
                float t = Mathf.Clamp01(job.elapsed / Mathf.Max(0.01f, tuning.mergePullDuration));
                float eased = Mathf.Pow(t, tuning.mergePullEase);   // slow first, then snap in

                Rigidbody2D body = job.small.GetComponent<Rigidbody2D>();
                if (body != null)
                {
                    body.MovePosition(Vector2.Lerp(job.startPosition, job.keeper.transform.position, eased));
                }

                job.small.transform.localScale = Vector3.Lerp(job.startScale, Vector3.zero, eased);

                if (t < 1f) continue;

                jobs.RemoveAt(i);

                float gain = job.small.Mass;
                float keeperBefore = job.keeper.Mass;
                job.keeper.AddMass(gain);
                job.small.Die(job.keeper);
            }
        }

        //for  Mergeing but have AI eat the main ball to remove the action
        private void Abort(Job job)
        {
            SizeController small = job.small;
            if (small == null)
            {
                return;
            }

            merging.Remove(small);

            Rigidbody2D body = small.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.position = job.startPosition;
                body.linearVelocity = Vector2.zero;
            }

            small.ApplySizeNow();

            PlayerSubBall sub = small.GetComponent<PlayerSubBall>();
            if (sub != null)
            {
                sub.enabled = true;
            }

            Collider2D[] colliders = small.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = true;
            }
        }
    }
}
