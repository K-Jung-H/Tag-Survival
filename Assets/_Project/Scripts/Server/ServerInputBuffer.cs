using System.Collections.Generic;
using UnityEngine;

public readonly struct ServerPlayerInputCommand
{
    public readonly ushort inputSeq;
    public readonly Vector2 input;
    public readonly Vector2 aim;
    public readonly PlayerInputButtons buttons;

    public ServerPlayerInputCommand(
        ushort inputSeq,
        Vector2 input,
        Vector2 aim,
        PlayerInputButtons buttons)
    {
        this.inputSeq = inputSeq;
        this.input = input;
        this.aim = aim;
        this.buttons = buttons;
    }
}

public sealed class ServerInputBuffer
{
    private const ushort NoReceivedInputSeq = ushort.MaxValue;

    private readonly Dictionary<ulong, ServerPlayerInputCommand> pendingInputs = new();
    private readonly Dictionary<ulong, ushort> latestReceivedInputSeqs = new();

    public void RegisterPlayer(ulong clientId)
    {
        pendingInputs.Remove(clientId);
        latestReceivedInputSeqs[clientId] = NoReceivedInputSeq;
    }

    public void RemovePlayer(ulong clientId)
    {
        pendingInputs.Remove(clientId);
        latestReceivedInputSeqs.Remove(clientId);
    }

    public bool SetInput(
        ulong clientId,
        ushort inputSeq,
        Vector2 input,
        Vector2 aim,
        PlayerInputButtons buttons)
    {
        if (!latestReceivedInputSeqs.TryGetValue(clientId, out ushort latestReceivedInputSeq))
        {
            return false;
        }

        if (!IsNewerInput(inputSeq, latestReceivedInputSeq))
        {
            return false;
        }

        if (input.sqrMagnitude > 1f)
        {
            input.Normalize();
        }

        if (aim.sqrMagnitude > 1f)
        {
            aim.Normalize();
        }

        pendingInputs[clientId] = new ServerPlayerInputCommand(inputSeq, input, aim, buttons);
        latestReceivedInputSeqs[clientId] = inputSeq;
        return true;
    }

    public bool TryConsumeInput(ulong clientId, out ServerPlayerInputCommand command)
    {
        if (!pendingInputs.TryGetValue(clientId, out command))
        {
            return false;
        }

        pendingInputs.Remove(clientId);
        return true;
    }

    private static bool IsNewerInput(ushort incomingSeq, ushort currentSeq)
    {
        if (incomingSeq == currentSeq)
        {
            return false;
        }

        return unchecked((short)(incomingSeq - currentSeq)) > 0;
    }
}
