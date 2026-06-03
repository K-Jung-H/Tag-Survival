using System.Collections.Generic;
using UnityEngine;

public sealed class SkillWorldContributionCollector
{
    private readonly List<PortalPairWorldContribution> portalPairs = new();

    public int PortalPairCount => portalPairs.Count;

    public PortalPairWorldContribution GetPortalPair(int index)
    {
        return portalPairs[index];
    }

    // Role: 이번 서버 시뮬레이션 tick에서 스킬이 등록한 월드 상호작용 정보를 비운다.
    public void Clear()
    {
        portalPairs.Clear();
    }

    // Role: 활성화된 포탈 쌍을 서버 월드 상호작용 대상으로 등록한다.
    // Parameters:
    // - first: 포탈 쌍의 첫 번째 endpoint
    // - second: 포탈 쌍의 두 번째 endpoint
    public void AddPortalPair(PortalEndpointWorldContribution first, PortalEndpointWorldContribution second)
    {
        portalPairs.Add(new PortalPairWorldContribution(first, second));
    }
}

public readonly struct PortalPairWorldContribution
{
    public readonly ulong ownerClientId;
    public readonly PortalEndpointWorldContribution first;
    public readonly PortalEndpointWorldContribution second;

    public PortalPairWorldContribution(
        PortalEndpointWorldContribution first,
        PortalEndpointWorldContribution second)
    {
        ownerClientId = first.ownerClientId;
        this.first = first;
        this.second = second;
    }
}

public readonly struct PortalEndpointWorldContribution
{
    public readonly ulong ownerClientId;
    public readonly byte skillObjectId;
    public readonly Vector2 position;
    public readonly Vector2 halfExtent;
    public readonly float teleportCooldownSeconds;

    public PortalEndpointWorldContribution(
        ulong ownerClientId,
        byte skillObjectId,
        Vector2 position,
        Vector2 halfExtent,
        float teleportCooldownSeconds)
    {
        this.ownerClientId = ownerClientId;
        this.skillObjectId = skillObjectId;
        this.position = position;
        this.halfExtent = halfExtent;
        this.teleportCooldownSeconds = teleportCooldownSeconds;
    }
}
