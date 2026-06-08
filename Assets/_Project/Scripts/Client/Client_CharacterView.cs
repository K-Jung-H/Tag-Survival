using UnityEngine;
using CharacterRenderState = CharacterRuntimeState;
using UnityEngine.Animations;
using UnityEngine.Playables;

public sealed class Client_CharacterView : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer body;
    [SerializeField] private SpriteRenderer aimLine;
    [SerializeField] private SpriteRenderer skillIndicator;
    [SerializeField] private Transform nameplateAnchor;
    [SerializeField] private Vector2 playerSize = new Vector2(0.8f, 0.8f);
    [SerializeField] private float aimLineLength = 1.8f;
    [SerializeField] private float aimLineWidth = 0.05f;

    private ICharacterStateMachine stateMachine;
    private CharacterAnimationData animationData;
    private Vector2 renderPosition;
    private bool hasRenderPosition;
    private PlayableGraph animationGraph;
    private AnimationClipPlayable clipPlayable;
    private LocomotionState currentClipState;
    private bool hasCurrentClipState;
    private bool hasMissingAnimationDataWarning;
    private bool hasMissingClipWarning;
    private Color defaultBodyColor = Color.white;
    private RuntimeAnimatorController currentAnimatorController;

    public ICharacterStateMachine StateMachine => stateMachine;
    public CharacterAnimationData AnimationData => animationData;
    public Transform NameplateAnchor => nameplateAnchor != null ? nameplateAnchor : transform;

    // - Role: Set up needed links before start.
    private void Awake()
    {
        ResolveNameplateAnchor();
    }

    // - Role: Check editor values after they change.
    private void OnValidate()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (body == null)
        {
            body = GetComponent<SpriteRenderer>();
        }

        ResolveNameplateAnchor();
    }

    // - Role: Find nameplate anchor.
    private void ResolveNameplateAnchor()
    {
        if (nameplateAnchor == null)
        {
            Transform foundAnchor = transform.Find("NameplateAnchor");
            if (foundAnchor != null)
            {
                nameplateAnchor = foundAnchor;
            }
        }
    }

    // - Role: Set the first state.
    public void Initialize(ulong clientId, CharacterDefinition definition)
    {
        byte characterId = definition != null ? definition.CharacterId : (byte)0;
        animationData = definition != null ? definition.AnimationData : null;
        ApplyAnimatorController(animationData);
        stateMachine = new CharacterStateMachine_Default(characterId);
        stateMachine.ApplyState(new CharacterRenderState
        {
            clientId = clientId,
            characterId = characterId,
            locomotionState = LocomotionState.Idle,
            aim = Vector2.right,
            facingSign = 1,
        });

        hasRenderPosition = false;
        hasCurrentClipState = false;

        if (body != null)
        {
            defaultBodyColor = body.color;
        }
    }

    // - Role: Clean up links before this object is destroyed.
    private void OnDestroy()
    {
        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }
    }

    // - Role: Apply snapshot.
    public void ApplySnapshot(
        ClientSnapshotState snapshotState,
        bool isLocalPlayer,
        float deltaTime,
        float localFollowSpeed,
        float remoteFollowSpeed,
        float snapDistance)
    {
        if (stateMachine == null || stateMachine.State.characterId != snapshotState.characterId)
        {
            stateMachine = new CharacterStateMachine_Default(snapshotState.characterId);
        }

        stateMachine.ApplySnapshotState(snapshotState);

        renderPosition = SmoothRenderPosition(
            snapshotState.position,
            isLocalPlayer ? localFollowSpeed : remoteFollowSpeed,
            snapDistance,
            deltaTime);
        hasRenderPosition = true;

        transform.position = new Vector3(renderPosition.x, renderPosition.y, transform.position.z);
        UpdateFacing(stateMachine.State);
        UpdateAimLine(snapshotState.aim, snapshotState.buttons);
        UpdateSkillIndicator();
        PlayLocomotionClip(stateMachine.State.locomotionState);
    }

    // - Role: Apply tagger color.
    public void ApplyTaggerColor(bool isTagger, Color taggerColor)
    {
        if (body != null)
        {
            body.color = isTagger ? taggerColor : defaultBodyColor;
        }
    }

    // - Role: Smooth the render position.
    private Vector2 SmoothRenderPosition(
        Vector2 targetPosition,
        float followSpeed,
        float snapDistance,
        float deltaTime)
    {
        if (!hasRenderPosition)
        {
            return targetPosition;
        }

        float distance = Vector2.Distance(renderPosition, targetPosition);
        if (distance >= Mathf.Max(0f, snapDistance))
        {
            return targetPosition;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0f, followSpeed) * deltaTime);
        return Vector2.Lerp(renderPosition, targetPosition, t);
    }

    // - Role: Update aim line.
    private void UpdateAimLine(Vector2 aim, PlayerInputButtons buttons)
    {
        if (aimLine == null)
        {
            return;
        }

        bool isSkillAimActive = (buttons & PlayerInputButtons.SkillAim) != 0;
        if (!isSkillAimActive || aim.sqrMagnitude < 0.0001f)
        {
            aimLine.enabled = false;
            return;
        }

        aim.Normalize();

        float scaleX = playerSize.x != 0f ? playerSize.x : 1f;
        float scaleY = playerSize.y != 0f ? playerSize.y : 1f;
        Transform lineTransform = aimLine.transform;

        lineTransform.localPosition = new Vector3(
            aim.x * aimLineLength * 0.5f / scaleX,
            aim.y * aimLineLength * 0.5f / scaleY,
            0f);

        lineTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg);

        lineTransform.localScale = new Vector3(aimLineLength / scaleX, aimLineWidth / scaleY, 1f);

        aimLine.enabled = true;
    }

    // - Role: Update skill indicator.
    private void UpdateSkillIndicator()
    {
        if (skillIndicator == null)
        {
            return;
        }

        skillIndicator.enabled = false;
    }

    // - Role: Apply animator controller.
    private void ApplyAnimatorController(CharacterAnimationData data)
    {
        if (animator == null)
        {
            return;
        }

        RuntimeAnimatorController nextController = data != null ? data.AnimatorController : null;
        if (currentAnimatorController == nextController
            && animator.runtimeAnimatorController == nextController)
        {
            return;
        }

        if (animationGraph.IsValid())
        {
            animationGraph.Stop();
        }

        animator.runtimeAnimatorController = nextController;
        animator.Rebind();
        animator.Update(0f);
        currentAnimatorController = nextController;
        hasCurrentClipState = false;
        hasMissingAnimationDataWarning = false;
        hasMissingClipWarning = false;
    }

    // - Role: Update facing.
    private void UpdateFacing(CharacterRenderState state)
    {
        if (body == null)
        {
            return;
        }

        body.flipX = state.facingSign < 0;
    }

    // - Role: Play locomotion clip.
    private void PlayLocomotionClip(LocomotionState locomotionState)
    {
        if (animator == null || animationData == null)
        {
            if (!hasMissingAnimationDataWarning)
            {
                Debug.LogWarning("[Client_CharacterView] CharacterAnimationData is not assigned.", this);
                hasMissingAnimationDataWarning = true;
            }

            return;
        }

        if (hasCurrentClipState && currentClipState == locomotionState)
        {
            return;
        }

        AnimationClip clip = animationData.GetClip(locomotionState);
        if (clip == null)
        {
            if (!hasMissingClipWarning)
            {
                Debug.LogWarning($"[Client_CharacterView] AnimationClip is not assigned for {locomotionState}.", this);
                hasMissingClipWarning = true;
            }

            return;
        }

        if (TryPlayAnimatorState(clip.name, locomotionState))
        {
            return;
        }

        EnsureAnimationGraph();

        if (clipPlayable.IsValid())
        {
            clipPlayable.Destroy();
        }

        clipPlayable = AnimationClipPlayable.Create(animationGraph, clip);
        clipPlayable.SetApplyFootIK(false);
        clipPlayable.SetSpeed(1d);
        AnimationPlayableOutput output = (AnimationPlayableOutput)animationGraph.GetOutput(0);
        output.SetSourcePlayable(clipPlayable);

        currentClipState = locomotionState;
        hasCurrentClipState = true;

        if (!animationGraph.IsPlaying())
        {
            animationGraph.Play();
        }
    }

    // - Role: Try to play animator state.
    private bool TryPlayAnimatorState(string stateName, LocomotionState locomotionState)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(stateName))
        {
            return false;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (!animator.HasState(0, stateHash))
        {
            return false;
        }

        if (animationGraph.IsValid())
        {
            animationGraph.Stop();
        }

        animator.Play(stateHash, 0, 0f);
        currentClipState = locomotionState;
        hasCurrentClipState = true;
        return true;
    }

    // - Role: Make sure the animation graph exists.
    private void EnsureAnimationGraph()
    {
        if (animationGraph.IsValid())
        {
            return;
        }

        animationGraph = PlayableGraph.Create($"{name}_CharacterAnimationGraph");
        animationGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        AnimationPlayableOutput.Create(animationGraph, "CharacterAnimation", animator);
    }

}
