using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    /// <summary>
    /// Pool for mass a ball spat out.
    ///
    /// Spores used to be created and Destroy()ed every time the button was held, which meant a
    /// long press produced a steady stream of garbage. They now keep their Rigidbody2D and are
    /// reset on every reuse, so holding the button does not allocate anything after warm up.
    /// </summary>
    public static class SporePool
    {
        private static readonly Stack<Edible> free = new Stack<Edible>(128);
        private static Transform root;
        private static int created;

        public static int Created { get { return created; } }
        public static int FreeCount { get { return free.Count; } }

        private static Transform Root
        {
            get
            {
                if (root == null)
                {
                    GameObject container = new GameObject("[SporePool]");
                    root = container.transform;
                }

                return root;
            }
        }

        public static Edible Take()
        {
            while (free.Count > 0)
            {
                Edible pooled = free.Pop();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            GameObject go = BallFactory.CreateSporeShell();
            created++;
            return go.GetComponent<Edible>();
        }

        public static void Give(Edible edible)
        {
            if (edible == null)
            {
                return;
            }

            edible.gameObject.SetActive(false);
            edible.transform.SetParent(Root, false);
            free.Push(edible);
        }
    }
}
