public static class PlayerPhysicsModifierResolver
{
    // - Role: Find ground.
    public static StagePhysicsModifier ResolveGround(
        PlayerObject player,
        StageDefinition stageDefinition)
    {
        if (!player.isGrounded)
        {
            return ResolveSurface(stageDefinition, StageSurfacePhysicType.Normal);
        }

        return ResolveSurface(stageDefinition, player.groundSurfacePhysicType);
    }

    // - Role: Find jump.
    public static StagePhysicsModifier ResolveJump(
        PlayerObject player,
        StageDefinition stageDefinition)
    {
        if (player.isGrounded || player.coyoteTimeRemaining > 0f)
        {
            return ResolveSurface(stageDefinition, player.groundSurfacePhysicType);
        }

        return ResolveSurface(stageDefinition, StageSurfacePhysicType.Normal);
    }

    // - Role: Find wall.
    public static StagePhysicsModifier ResolveWall(
        PlayerObject player,
        StageDefinition stageDefinition)
    {
        if (!player.isWallSticking)
        {
            return ResolveSurface(stageDefinition, StageSurfacePhysicType.Normal);
        }

        return ResolveSurface(stageDefinition, player.wallSurfacePhysicType);
    }

    // - Role: Find air.
    public static StagePhysicsModifier ResolveAir(StageDefinition stageDefinition)
    {
        return ResolveSurface(stageDefinition, StageSurfacePhysicType.Normal);
    }

    // - Role: Find surface.
    public static StagePhysicsModifier ResolveSurface(
        StageDefinition stageDefinition,
        StageSurfacePhysicType surfacePhysicType)
    {
        if (stageDefinition == null)
        {
            return StagePhysicsModifier.Normal;
        }

        return stageDefinition.ResolvePhysicsModifier(surfacePhysicType);
    }
}
