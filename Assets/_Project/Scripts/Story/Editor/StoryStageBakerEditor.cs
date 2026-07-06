#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class StoryStageBakerWindow : EditorWindow
{
    [SerializeField] private StoryStageConfig output;
    [SerializeField] private StageDefinition stageDefinition;
    [SerializeField] private StageRenderBinding stageRender;
    [SerializeField] private StorySpawnMarker playerSpawn;
    [SerializeField] private StoryGoalMarker goal;
    [SerializeField] private bool showConfig = true;
    [SerializeField] private bool showReferences = true;
    [SerializeField] private bool showPreview = true;

    private Vector2 scrollPosition;

    [MenuItem("Tools/StoryStageBaker")]
    public static void Open()
    {
        StoryStageBakerWindow window = GetWindow<StoryStageBakerWindow>("StoryStageBaker");
        window.Show();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        DrawOutputSection();
        DrawReferenceSection();
        DrawPreviewSection();
        DrawConfigSection();
        DrawActionSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawOutputSection()
    {
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        output = (StoryStageConfig)EditorGUILayout.ObjectField("Story Stage Config", output, typeof(StoryStageConfig), false);
        if (EditorGUI.EndChangeCheck() && output != null && stageDefinition == null)
        {
            stageDefinition = output.StageDefinition;
        }

        EditorGUILayout.Space(8f);
    }

    private void DrawReferenceSection()
    {
        showReferences = EditorGUILayout.Foldout(showReferences, "Bake References", true, EditorStyles.foldoutHeader);
        if (!showReferences)
        {
            EditorGUILayout.Space(8f);
            return;
        }

        stageDefinition = (StageDefinition)EditorGUILayout.ObjectField("Stage Definition", stageDefinition, typeof(StageDefinition), false);
        stageRender = (StageRenderBinding)EditorGUILayout.ObjectField("Stage Render", stageRender, typeof(StageRenderBinding), true);
        playerSpawn = (StorySpawnMarker)EditorGUILayout.ObjectField("Player Spawn", playerSpawn, typeof(StorySpawnMarker), true);
        goal = (StoryGoalMarker)EditorGUILayout.ObjectField("Goal", goal, typeof(StoryGoalMarker), true);
        EditorGUILayout.Space(8f);
    }

    private void DrawPreviewSection()
    {
        showPreview = EditorGUILayout.Foldout(showPreview, "Bake Preview", true, EditorStyles.foldoutHeader);
        if (!showPreview)
        {
            EditorGUILayout.Space(8f);
            return;
        }

        StoryStageBakeRequest request = BuildRequest();
        StoryStageBakeReport report = StoryStageBaker.Validate(request);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawPreviewLine("Stage Name", ResolveStageName());
        DrawStageBakeDataPreview();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel);
        DrawMarkerPreview(report);
        EditorGUILayout.EndVertical();

        DrawEnemyPreview();
        DrawReportMessages(report);
        EditorGUILayout.Space(8f);
    }

    private void DrawStageBakeDataPreview()
    {
        StageBakeData bakeData = stageDefinition != null ? stageDefinition.StageBakeData : null;
        if (bakeData == null)
        {
            DrawPreviewLine("Stage Offset", "-");
            DrawPreviewLine("Cell Size", "-");
            return;
        }

        DrawPreviewLine("Stage Offset", FormatVector(bakeData.StageOffsetPosition));
        DrawPreviewLine("Cell Size", bakeData.CellSize.ToString("0.###"));
    }

    private void DrawMarkerPreview(StoryStageBakeReport report)
    {
        DrawPreviewLine(
            "World Pos",
            playerSpawn != null ? FormatVector(playerSpawn.transform.position) : "-");
        DrawPreviewLine(
            "Baked Pos",
            report.hasPlayerSpawnPosition ? FormatVector(report.playerSpawnPosition) : "-");

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Goal", EditorStyles.boldLabel);
        DrawPreviewLine(
            "World Pos",
            goal != null ? FormatVector(goal.transform.position) : "-");
        DrawPreviewLine(
            "Baked Pos",
            report.hasGoal ? FormatVector(report.goal.position) : "-");
        DrawPreviewLine(
            "Collider Offset",
            report.hasGoal ? FormatVector(report.goal.colliderOffset) : "-");
        DrawPreviewLine(
            "Collider Size",
            report.hasGoal ? FormatVector(report.goal.colliderSize) : "-");
    }

    private void DrawConfigSection()
    {
        showConfig = EditorGUILayout.Foldout(showConfig, "Story Stage Config", true, EditorStyles.foldoutHeader);
        if (!showConfig)
        {
            EditorGUILayout.Space(8f);
            return;
        }

        if (output == null)
        {
            EditorGUILayout.HelpBox("Assign Story Stage Config to edit time limits.", MessageType.Info);
            EditorGUILayout.Space(8f);
            return;
        }

        SerializedObject serializedOutput = new SerializedObject(output);
        SerializedProperty stageTimeLimit = serializedOutput.FindProperty("stageTimeLimitSeconds");
        SerializedProperty bonusTimeLimit = serializedOutput.FindProperty("bonusStarTimeLimitSeconds");

        serializedOutput.Update();
        EditorGUILayout.PropertyField(stageTimeLimit, new GUIContent("Time Limit"));
        EditorGUILayout.PropertyField(bonusTimeLimit, new GUIContent("Bonus Time Limit"));
        serializedOutput.ApplyModifiedProperties();
        EditorGUILayout.Space(8f);
    }

    private void DrawActionSection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Validate"))
            {
                LogReport("Story stage validation", StoryStageBaker.Validate(BuildRequest()));
            }

            if (GUILayout.Button("Bake"))
            {
                LogReport("Story stage bake", StoryStageBaker.Bake(BuildRequest()));
            }
        }
    }

    private void DrawReportMessages(StoryStageBakeReport report)
    {
        foreach (string error in report.errors)
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        foreach (string warning in report.warnings)
        {
            EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }
    }

    private StoryStageBakeRequest BuildRequest()
    {
        return new StoryStageBakeRequest
        {
            output = output,
            stageDefinition = stageDefinition,
            stageRender = stageRender,
            playerSpawn = playerSpawn,
            goal = goal
        };
    }

    private string ResolveStageName()
    {
        if (stageDefinition != null)
        {
            return stageDefinition.name;
        }

        return output != null ? output.StageId : "-";
    }

    private void DrawEnemyPreview()
    {
        if (output == null)
        {
            return;
        }

        StoryEnemySpawnData[] enemies = output.Enemies;
        if (enemies.Length == 0)
        {
            return;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Enemies", EditorStyles.boldLabel);
        for (int i = 0; i < enemies.Length; i++)
        {
            StoryEnemySpawnData enemy = enemies[i];
            DrawPreviewLine(
                $"Enemy {i}",
                $"type: {enemy.characterId}, id: {enemy.id}, pos: {FormatVector(enemy.position)}");
        }

        EditorGUILayout.EndVertical();
    }

    private static void DrawPreviewLine(string label, string value)
    {
        EditorGUILayout.LabelField(label, value);
    }

    private static string FormatVector(Vector2 value)
    {
        return $"({value.x:0.###}, {value.y:0.###})";
    }

    private static string FormatVector(Vector2Int value)
    {
        return $"({value.x}, {value.y})";
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";
    }

    private static void LogReport(string label, StoryStageBakeReport report)
    {
        foreach (string error in report.errors)
        {
            Debug.LogError(error);
        }

        foreach (string warning in report.warnings)
        {
            Debug.LogWarning(warning);
        }

        if (report.HasErrors)
        {
            Debug.LogError($"{label} failed.");
            return;
        }

        Debug.Log(
            $"{label} complete. " +
            $"stage: {report.stageName}, " +
            $"spawn: {report.playerSpawnPosition}, " +
            $"goal: {report.goal.position}, " +
            $"goal collider offset: {report.goal.colliderOffset}, " +
            $"goal collider size: {report.goal.colliderSize}.");
    }
}
#endif
