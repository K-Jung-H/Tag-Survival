public static class PlayerPhysicsModifierResolver
{
    // - Role: Find ground.
    public static StagePhysicsModifier ResolveGround(
        PlayerObject player,
        StageDefinition stageDefinition)
    {
        if (!player.isGrounded)
        {
            return ResolveSurface(stageDefinition, StageSurfaceType.Normal);
        }

        return ResolveSurface(stageDefinition, player.groundSurface);
    }

    // - Role: Find jump.
    public static StagePhysicsModifier ResolveJump(
        PlayerObject player,
        StageDefinition stageDefinition)
    {
        if (player.isGrounded || player.lateJumpTimer > 0f)
        {
            return ResolveSurface(stageDefinition, player.groundSurface);
        }

        return ResolveSurface(stageDefinition, StageSurfaceType.Normal);
    }

    // - Role: Find wall.
    public static StagePhysicsModifier ResolveWall(
        PlayerObject player,
        StageDefinition stageDefinition)
    {
        if (!player.isOnWall)
        {
            return ResolveSurface(stageDefinition, StageSurfaceType.Normal);
        }

        return ResolveSurface(stageDefinition, player.wallSurface);
    }

    // - Role: Find air.
    public static StagePhysicsModifier ResolveAir(StageDefinition stageDefinition)
    {
        return ResolveSurface(stageDefinition, StageSurfaceType.Normal);
    }

    // - Role: Find surface.
    public static StagePhysicsModifier ResolveSurface(
        StageDefinition stageDefinition,
        StageSurfaceType surfacePhysicType)
    {
        if (stageDefinition == null)
        {
            return StagePhysicsModifier.Normal;
        }

        return stageDefinition.ResolvePhysicsModifier(surfacePhysicType);
    }
}
