using System;

[Serializable]
public struct GameSessionPlayerProfile
{
    public ulong clientId;
    public string nickname;
    public byte characterId;
    public byte skillId;
}
