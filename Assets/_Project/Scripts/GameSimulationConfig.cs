public static class GameSimulationConfig
{
    public const float PlayerMoveSpeed = 7f;
    public const float PlayerRadius = 0.4f;
    public const float CollisionSkinWidth = 0.02f;
    public const int MovementSubSteps = 3;
    public const float PredictionReplayDeltaTime = 1f / GameNetProtocol.InputSendRate;
}