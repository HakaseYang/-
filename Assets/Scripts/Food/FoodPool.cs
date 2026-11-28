using System.Collections.Generic;
using UnityEngine;

namespace QiuQiu
{

    public static class FoodPool
    {
        private static readonly Stack<Edible> free = new Stack<Edible>(512);
        private static Transform root;
        private static int created;

        public static int Created { get { return created; } }
        public static int FreeCount { get { return free.Count; } }
        public static void UseRoot(Transform container)
        {
            if (container != root)
            {
                free.Clear();
                created = 0;
            }

            root = container;
        }

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
                Edible pooled = free.Pop();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            GameObject go = BallFactory.CreateFoodShell();
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

        public static void Prewarm(int count)
        {
            while (created < count)
            {
                GameObject go = BallFactory.CreateFoodShell();
                created++;
                Give(go.GetComponent<Edible>());
            }
        }
    }
}
