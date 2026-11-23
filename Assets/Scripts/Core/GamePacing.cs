namespace QiuQiu
{
    // One place for the global pacing knobs, so "make the whole game slower" is a single edit
    // instead of hunting through six scripts.
    //
    // RULE: every multiplier is applied exactly ONCE, at the point where that speed is handed
    // to the Rigidbody. If two places multiplied the same number the game would quietly run at
    // 0.6 x 0.7 and nobody could tune it back.
    //
    //   1.0    = keep the value written in the Inspector
    //   0.8    = that part of the game runs 20% slower
    //
    // The per-script values are still the fine tuning; these are the "whole game" dials.
    public static class GamePacing
    {
        // where the multiplier is applied:
        public const float PlayerMove = 1f;        // PlayerController.GroupVelocity
        public const float AiMove = 1f;            // AIController.ApplyMovement (speed)
        public const float AiAcceleration = 1f;    // AIController.ApplyMovement (acceleration)
        public const float CellFollow = 1f;        // PlayerCellMotion.ApplyMotionTuning
        public const float SplitLaunch = 1f;       // PlayerActions.TrySplit (bullet impulse)
        public const float VirusBurstLaunch = 1f;  // Virus.LaunchBurstPiece
        public const float SpitLaunch = 1f;        // PlayerActions.TrySpit
        public const float MergeDuration = 1f;     // PlayerMerge.Begin (animation length)
        public const float CameraSmooth = 1f;      // CameraFollow (smooth times)
    }
}
