using UnityEngine;

public sealed class Client_CharacterAudioView : MonoBehaviour
{
    [SerializeField] private GameFeedbackCatalog feedbackCatalog;
    [SerializeField] private AudioSource oneShotAudioSource;
    [SerializeField] private Transform audioListenerTransform;

    private LocomotionState previousLocomotionState;
    private bool hasSnapshot;
    private float runStepTimer;

    // - Role: Bind scene-level audio listener transform.
    public void BindAudioListener(Transform listenerTransform)
    {
        audioListenerTransform = listenerTransform;
        UpdateWorldAudioPosition(oneShotAudioSource);
    }

    // - Role: Apply character snapshot driven audio.
    public void ApplySnapshot(ClientSnapshotState snapshotState)
    {
        LocomotionState currentState = snapshotState.locomotionState;
        UpdateWorldAudioPosition(oneShotAudioSource);
        float deltaTime = Time.deltaTime;

        if (!hasSnapshot)
        {
            hasSnapshot = true;
            previousLocomotionState = currentState;
            UpdateRunStep(currentState, deltaTime);
            return;
        }

        PlayStateEnterFeedback(previousLocomotionState, currentState);
        UpdateRunStep(currentState, deltaTime);

        previousLocomotionState = currentState;
    }

    // - Role: Play supplied feedback data on this character.
    public float PlayFeedback(GameFeedbackData data)
    {
        return PlayOneShot(data);
    }

    // - Role: Reset cached state when character data is rebuilt.
    public void ResetAudioState()
    {
        hasSnapshot = false;
        previousLocomotionState = LocomotionState.Idle;
        runStepTimer = 0f;
    }

    // - Role: Play one-shot feedback for state transitions.
    private void PlayStateEnterFeedback(LocomotionState previousState, LocomotionState currentState)
    {
        if (currentState == LocomotionState.Jump && previousState != LocomotionState.Jump)
        {
            PlayOneShot(ClientFeedbackType.CharacterJump);
            return;
        }

        if (currentState == LocomotionState.WallStick && previousState != LocomotionState.WallStick)
        {
            PlayOneShot(ClientFeedbackType.CharacterWallStick);
            return;
        }

        if (IsLandingTransition(previousState, currentState))
        {
            PlayOneShot(ClientFeedbackType.CharacterLand);
        }
    }

    // - Role: Check land transition.
    private static bool IsLandingTransition(LocomotionState previousState, LocomotionState currentState)
    {
        return IsAirborneState(previousState)
            && (currentState == LocomotionState.Idle || currentState == LocomotionState.Run);
    }

    // - Role: Check states that can produce land feedback.
    private static bool IsAirborneState(LocomotionState state)
    {
        return state == LocomotionState.Jump
            || state == LocomotionState.Fall
            || state == LocomotionState.WallStick;
    }

    // - Role: Update repeated run step feedback by locomotion state.
    private void UpdateRunStep(LocomotionState currentState, float deltaTime)
    {
        if (currentState != LocomotionState.Run)
        {
            runStepTimer = 0f;
            return;
        }

        runStepTimer -= deltaTime;
        if (runStepTimer > 0f)
        {
            return;
        }

        runStepTimer = PlayOneShot(ClientFeedbackType.CharacterRunStep);
    }

    // - Role: Play cloned one-shot sound from feedback data and return its interval.
    private float PlayOneShot(ClientFeedbackType feedbackType)
    {
        if (oneShotAudioSource == null
            || feedbackCatalog == null
            || !feedbackCatalog.TryGet(feedbackType, out ClientFeedbackProfile profile)
            || profile.data.sound.clip == null)
        {
            return 0f;
        }

        return PlayOneShot(profile.data);
    }

    // - Role: Play cloned one-shot sound data and return its interval.
    private float PlayOneShot(GameFeedbackData data)
    {
        if (oneShotAudioSource == null || data.sound.clip == null)
        {
            return 0f;
        }

        Transform parent = oneShotAudioSource.transform.parent;
        AudioSource audioSource = Instantiate(oneShotAudioSource, parent);
        audioSource.gameObject.SetActive(true);
        audioSource.Stop();
        ApplySoundSettings(audioSource, data.sound);
        audioSource.loop = false;
        audioSource.clip = data.sound.clip;
        audioSource.Play();

        float lifetime = ResolveSoundLifetime(data.sound.clip, audioSource, data.lifetimeSeconds);
        Destroy(audioSource.gameObject, lifetime);
        return lifetime;
    }

    // - Role: Resolve playback limit.
    private static float ResolveSoundLifetime(AudioClip clip, AudioSource audioSource, float lifetimeSeconds)
    {
        if (lifetimeSeconds > 0f)
        {
            return lifetimeSeconds;
        }

        float pitch = audioSource != null ? Mathf.Abs(audioSource.pitch) : 1f;
        return clip.length / Mathf.Max(0.01f, pitch) + 0.1f;
    }

    // - Role: Apply common sound settings.
    private void ApplySoundSettings(AudioSource audioSource, GameFeedbackSound sound)
    {
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = sound.space == GameFeedbackSoundSpace.World ? 1f : 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.volume = sound.Volume;

        if (sound.space == GameFeedbackSoundSpace.World)
        {
            UpdateWorldAudioPosition(audioSource);
        }
    }

    // - Role: Keep world audio on the listener plane in a 2D stage.
    private void UpdateWorldAudioPosition(AudioSource audioSource)
    {
        if (audioSource == null)
        {
            return;
        }

        if (audioListenerTransform == null)
        {
            return;
        }

        Vector3 ownerPosition = transform.position;
        audioSource.transform.position = new Vector3(
            ownerPosition.x,
            ownerPosition.y,
            audioListenerTransform.position.z);
    }
}
