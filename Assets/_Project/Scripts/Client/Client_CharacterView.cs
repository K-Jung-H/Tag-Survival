using UnityEngine;
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
    private PlayerLocomotionState currentClipState;
    private bool hasCurrentClipState;
    private bool hasMissingAnimationDataWarning;
    private bool hasMissingClipWarning;
    private Color defaultBodyColor = Color.white;
    private RuntimeAnimatorController currentAnimatorController;

    public ICharacterStateMachine StateMachine => stateMachine;
    public CharacterAnimationData AnimationData => animationData;
    public Transform NameplateAnchor => nameplateAnchor != null ? nameplateAnchor : transform;

    private void Awake()
    {
        ResolveNameplateAnchor();
    }

    // Role: 인스펙터에서 누락된 렌더링 참조를 보조 연결한다.
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

    // Role: 캐릭터 정의와 클라이언트 ID를 사용해 View 런타임 상태를 초기화한다.
    // Parameters:
    // - clientId: 이 View가 표시할 클라이언트 ID
    // - definition: 캐릭터 View와 애니메이션 데이터 정의
    public void Initialize(ulong clientId, CharacterDefinition definition)
    {
        byte characterId = definition != null ? definition.CharacterId : (byte)0;
        animationData = definition != null ? definition.AnimationData : null;
        ApplyAnimatorController(animationData);
        stateMachine = CharacterStateMachineFactory.Create(characterId);
        stateMachine.ApplyState(new CharacterRuntimeState
        {
            clientId = clientId,
            characterId = characterId,
            locomotionState = PlayerLocomotionState.Idle,
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

    // Role: 생성한 애니메이션 Playable 리소스를 해제한다.
    private void OnDestroy()
    {
        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }
    }

    // Role: 클라이언트 스냅샷 상태를 View 위치, 조준선, Animator에 반영한다.
    // Parameters:
    // - snapshotState: 서버에서 수신한 플레이어 상태
    // - isLocalPlayer: 로컬 플레이어 여부
    // - deltaTime: 보간에 사용할 프레임 시간
    // - localFollowSpeed: 로컬 플레이어 위치 보간 속도
    // - remoteFollowSpeed: 원격 플레이어 위치 보간 속도
    // - snapDistance: 즉시 보정할 거리 기준
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
            stateMachine = CharacterStateMachineFactory.Create(snapshotState.characterId);
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

    // Role: Tagger 상태에 따라 Body 스프라이트 색상을 갱신한다.
    // Parameters:
    // - isTagger: 현재 플레이어가 Tagger인지 여부
    // - taggerColor: Tagger에게 적용할 색상
    public void ApplyTaggerColor(bool isTagger, Color taggerColor)
    {
        if (body != null)
        {
            body.color = isTagger ? taggerColor : defaultBodyColor;
        }
    }

    // Role: 현재 렌더 위치를 서버 위치 쪽으로 보간한다.
    // Parameters:
    // - targetPosition: 최신 서버 위치
    // - followSpeed: 위치 보간 속도
    // - snapDistance: 즉시 보정할 거리 기준
    // - deltaTime: 보간에 사용할 프레임 시간
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

    // Role: 조준 방향 선을 갱신한다.
    // Parameters:
    // - aim: 표시할 조준 방향
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

        lineTransform.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg);

        lineTransform.localScale = new Vector3(
            aimLineLength / scaleX,
            aimLineWidth / scaleY,
            1f);

        aimLine.enabled = true;
    }

    // Role: SkillIndicator is no longer used and stays hidden.
    private void UpdateSkillIndicator()
    {
        if (skillIndicator == null)
        {
            return;
        }

        skillIndicator.enabled = false;
    }

    // Role: CharacterAnimationData에 연결된 AnimatorController를 Body Animator에 반영한다.
    // Parameters:
    // - data: 캐릭터별 애니메이션 데이터
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

    // Role: 서버에서 전달된 바라보는 방향을 Body 스프라이트에 반영한다.
    // Parameters:
    // - state: 반영할 캐릭터 런타임 상태
    private void UpdateFacing(CharacterRuntimeState state)
    {
        if (body == null)
        {
            return;
        }

        body.flipX = state.facingSign < 0;
    }

    // Role: LocomotionState에 연결된 AnimationClip을 직접 재생한다.
    // Parameters:
    // - locomotionState: 재생할 이동 상태
    private void PlayLocomotionClip(PlayerLocomotionState locomotionState)
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
                Debug.LogWarning(
                    $"[Client_CharacterView] AnimationClip is not assigned for {locomotionState}.",
                    this);
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

    // Role: Animator Controller에 같은 이름의 State가 있으면 해당 State를 직접 재생한다.
    // Parameters:
    // - stateName: 재생할 Animator State 이름
    // - locomotionState: 현재 이동 상태
    private bool TryPlayAnimatorState(string stateName, PlayerLocomotionState locomotionState)
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

    // Role: AnimationClip 직접 재생에 사용할 PlayableGraph를 준비한다.
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
