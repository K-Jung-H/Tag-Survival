using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public sealed class ItemSelectionReceiver : MonoBehaviour
{
    private struct QueuedOffer
    {
        public float applyTime;
        public ItemSelectionOfferPacket packet;
    }

    private struct QueuedResult
    {
        public float applyTime;
        public ItemSelectionResultPacket packet;
    }

    private struct QueuedChoice
    {
        public float sendTime;
        public ItemSelectionChoicePacket packet;
    }

    [SerializeField] private Client_NetworkDelaySimulator networkDelaySimulator;

    private readonly List<QueuedOffer> queuedOffers = new();
    private readonly List<QueuedResult> queuedResults = new();
    private readonly List<QueuedChoice> queuedChoices = new();

    private FastBufferWriter choiceWriter;
    private bool choiceWriterCreated;
    private bool isRegistered;

    public event Action<ItemSelectionOfferPacket> OfferReceived;
    public event Action<ItemSelectionResultPacket> ResultReceived;

    // - Role: Set up this object before start.
    private void Awake()
    {
        if (networkDelaySimulator == null)
        {
            networkDelaySimulator = GetComponent<Client_NetworkDelaySimulator>();
        }

        choiceWriter = new FastBufferWriter(GameNetProtocol.ItemSelectionPacketBufferSize, Allocator.Persistent);
        choiceWriterCreated = true;
    }

    // - Role: Set up this object when it starts.
    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[ItemSelectionReceiver] NetworkManager.Singleton is null.", this);
            enabled = false;
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // - Role: Update this object each frame.
    private void Update()
    {
        TryRegisterHandlers();
        FlushQueuedOffers();
        FlushQueuedResults();
        FlushQueuedChoices();
    }

    // - Role: Clean up links before destroy.
    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        UnregisterHandlers();
        if (choiceWriterCreated)
        {
            choiceWriter.Dispose();
            choiceWriterCreated = false;
        }
    }

    // - Role: Send selected item id.
    public void SendChoice(uint requestId, int selectedId)
    {
        ItemSelectionChoicePacket packet = new ItemSelectionChoicePacket
        {
            protocolVersion = GameNetProtocol.ProtocolVersion,
            requestId = requestId,
            selectedId = selectedId
        };

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            queuedChoices.Add(new QueuedChoice
            {
                sendTime = Time.realtimeSinceStartup + delaySeconds,
                packet = packet
            });
            return;
        }

        SendChoiceNow(packet);
    }

    // - Role: Handle client connected.
    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
        {
            TryRegisterHandlers();
        }
    }

    // - Role: Handle client disconnected.
    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
        {
            return;
        }

        queuedOffers.Clear();
        queuedResults.Clear();
        queuedChoices.Clear();
        UnregisterHandlers();
    }

    // - Role: Try to register handlers.
    private void TryRegisterHandlers()
    {
        if (isRegistered || !CanUseClientMessages())
        {
            return;
        }

        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(GameNetMessages.ServerItemSelectionOffer, OnOfferReceived);
        NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(GameNetMessages.ServerItemSelectionResult, OnResultReceived);
        isRegistered = true;
    }

    // - Role: Unregister handlers.
    private void UnregisterHandlers()
    {
        if (!isRegistered || NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null)
        {
            return;
        }

        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerItemSelectionOffer);
        NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(GameNetMessages.ServerItemSelectionResult);
        isRegistered = false;
    }

    // - Role: Handle offer message.
    private void OnOfferReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseClientMessages())
        {
            return;
        }

        if (!ItemSelectionOfferPacket.TryRead(ref reader, out ItemSelectionOfferPacket packet))
        {
            return;
        }

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            queuedOffers.Add(new QueuedOffer
            {
                applyTime = Time.realtimeSinceStartup + delaySeconds,
                packet = packet
            });
            return;
        }

        OfferReceived?.Invoke(packet);
    }

    // - Role: Handle result message.
    private void OnResultReceived(ulong senderClientId, FastBufferReader reader)
    {
        if (!CanUseClientMessages())
        {
            return;
        }

        if (!ItemSelectionResultPacket.TryRead(ref reader, out ItemSelectionResultPacket packet))
        {
            return;
        }

        float delaySeconds = GetNetworkDelaySeconds();
        if (delaySeconds > 0f)
        {
            queuedResults.Add(new QueuedResult
            {
                applyTime = Time.realtimeSinceStartup + delaySeconds,
                packet = packet
            });
            return;
        }

        ResultReceived?.Invoke(packet);
    }

    // - Role: Flush queued offers.
    private void FlushQueuedOffers()
    {
        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < queuedOffers.Count; i++)
        {
            if (queuedOffers[i].applyTime > now)
            {
                continue;
            }

            ItemSelectionOfferPacket packet = queuedOffers[i].packet;
            queuedOffers.RemoveAt(i);
            i--;
            OfferReceived?.Invoke(packet);
        }
    }

    // - Role: Flush queued results.
    private void FlushQueuedResults()
    {
        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < queuedResults.Count; i++)
        {
            if (queuedResults[i].applyTime > now)
            {
                continue;
            }

            ItemSelectionResultPacket packet = queuedResults[i].packet;
            queuedResults.RemoveAt(i);
            i--;
            ResultReceived?.Invoke(packet);
        }
    }

    // - Role: Flush queued choices.
    private void FlushQueuedChoices()
    {
        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < queuedChoices.Count; i++)
        {
            if (queuedChoices[i].sendTime > now)
            {
                continue;
            }

            ItemSelectionChoicePacket packet = queuedChoices[i].packet;
            queuedChoices.RemoveAt(i);
            i--;
            SendChoiceNow(packet);
        }
    }

    // - Role: Send choice now.
    private void SendChoiceNow(ItemSelectionChoicePacket packet)
    {
        if (!CanUseClientMessages() || !choiceWriterCreated)
        {
            return;
        }

        choiceWriter.Truncate(0);
        packet.Write(ref choiceWriter);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            GameNetMessages.ClientItemSelectionChoice,
            NetworkManager.ServerClientId,
            choiceWriter,
            NetworkDelivery.ReliableSequenced);
    }

    // - Role: Check if client messages can be used.
    private static bool CanUseClientMessages()
    {
        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.IsClient || !NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsServer)
        {
            return false;
        }

        return NetworkManager.Singleton.CustomMessagingManager != null;
    }

    // - Role: Get network delay seconds.
    private float GetNetworkDelaySeconds()
    {
        return networkDelaySimulator != null ? networkDelaySimulator.OneWayDelaySeconds : 0f;
    }
}
