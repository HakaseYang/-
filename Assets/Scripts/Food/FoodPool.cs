using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{
    // Food churn is constant: every bite destroys an object and the spawner makes a new one.
    // This pool recycles the shells instead. Inactive entries do not count in Edible.ActiveCount,
    // so the spawner's "how much food is on the field" logic stays correct.
    public static class FoodPool
    {
        private static readonly Stack<Edible> free = new Stack<Edible>(512);
        private static Transform root;
        private static int created;

        public static int Created { get { return created; } }
        public static int FreeCount { get { return free.Count; } }

        // the container must stay active, otherwise reactivating a child would not render
        private static Transform Root
        {
            get
            {
                if (root == null)
                {
                    GameObject container = new GameObject("[FoodPool]");
                    root = container.transform;
                }

                return root;
            }
        }

        public static Edible Take()
        {
            while (free.Count > 0)
            {
                // a pooled entry can die with the scene, so skip Unity-fake-null entries
                Edible pooled = free.Pop();
                if (pooled != null) return pooled;
            }

            GameObject go = BallFactory.CreateFoodShell();
            created++;
            return go.GetComponent<Edible>();
        }

        public static void Give(Edible edible)
        {
            if (edible == null) return;

            // deactivating fires OnDisable, which decrements Edible.ActiveCount
            edible.gameObject.SetActive(false);
            edible.transform.SetParent(Root, false);
            free.Push(edible);
        }

        // shell count up front, so the first bites do not allocate
        public static void Prewarm(int count)
        {
            while (created < count)
            {
                GameObject go = BallFactory.CreateFoodShell();
                created++;
                Give(go.GetComponent<Edible>());
            }
        }

        // play mode without domain reload keeps statics alive; this clears the stale stack
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            free.Clear();
            root = null;
            created = 0;
        }
    }
}
