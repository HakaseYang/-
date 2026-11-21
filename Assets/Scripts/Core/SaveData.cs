using UnityEngine;

namespace QiuQiu
{
    // writes player progress to disk
    public static class SaveData
    {
        private const string BestMassKey = "QiuQiu.BestMass";

        public static float BestMass
        {
            get { return PlayerPrefs.GetFloat(BestMassKey); }
        }

        public static void ReportBestMass(float mass)
        {
            if (mass <= 0f || mass <= BestMass)
            {
                return;
            }

            PlayerPrefs.SetFloat(BestMassKey, mass);
            PlayerPrefs.Save();
        }
    }
}
