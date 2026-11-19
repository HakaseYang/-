using UnityEngine;

namespace QiuQiu
{

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CircleCollider2D))]
    public class Edible : MonoBehaviour
    {
        [SerializeField] private float mass = 1f;

        private bool consumed;    //  one frame > only one
        private float armedAt;    //  avoid  own spits the ball then get back at once
        private bool poolable;    //  set by FoodPool: recycle this instead of Destroy

        private static int activeCount; //allCount(now)


        public static int ActiveCount
        {
            get { return activeCount; }
        }

        private void OnEnable()
        {
            activeCount++;
        }

        private void OnDisable() 
        {
            activeCount--; 
        }

        // armDelay：wait time > armedAt
        public void Initialize(float massValue, float armDelay = 0f)
        {
            mass = Mathf.Max(0.01f, massValue);
            armedAt = Time.time + Mathf.Max(0f, armDelay);
        }

        // the pool hands this object out again: clear the state left by the last bite
        public void ResetForReuse(float massValue, float armDelay)
        {
            consumed = false;
            Initialize(massValue, armDelay);
        }

        public void MarkPoolable()
        {
            poolable = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (consumed) return;
            if (Time.time < armedAt) return;

            
            SizeController eater = other.GetComponentInParent<SizeController>();
            if (eater == null || eater.IsDead)
            {
                return;
            }

            consumed = true;
            eater.AddMassFromEat(mass);   

            if (poolable)
            {
                FoodPool.Give(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
