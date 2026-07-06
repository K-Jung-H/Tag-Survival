using System.Collections.Generic;
using UnityEngine;

public sealed class StoryStageBakeRequest
{
    public StoryStageConfig output;
    public StageDefinition stageDefinition;
    public StageRenderBinding stageRender;
    public StorySpawnMarker playerSpawn;
    public StoryGoalMarker goal;
}

public sealed class StoryStageBakeReport
{
    public string stageName;
    public bool hasPlayerSpawnPosition;
    public Vector2 playerSpawnPosition;
    public bool hasGoal;
    public StoryGoalData goal;
    public readonly List<string> warnings = new();
    public readonly List<string> errors = new();

    public bool HasErrors => errors.Count > 0;
}

public static class StoryStageBaker
{
    public static StoryStageBakeReport Validate(StoryStageBakeRequest request)
    {
        StoryStageBakeReport report = new StoryStageBakeReport();
        FillAvailablePreview(request, report);
        ValidateRequest(request, report);
        return report;
    }

#if UNITY_EDITOR
    public static StoryStageBakeReport Bake(StoryStageBakeRequest request)
    {
        StoryStageBakeReport report = Validate(request);
        if (report.HasErrors)
        {
            return report;
        }

        request.output.SetStageBakeResult(
            request.stageDefinition,
            report.playerSpawnPosition,
            report.goal);

        UnityEditor.EditorUtility.SetDirty(request.output);
        UnityEditor.AssetDatabase.SaveAssets();
        return report;
    }
#endif

    public static bool TryBuildPreview(StoryStageBakeRequest request, out Vector2 playerSpawnPosition, out StoryGoalData goalData)
    {
        playerSpawnPosition = default;
        goalData = default;

        StoryStageBakeReport report = Validate(request);
        if (report.HasErrors)
        {
            return false;
        }

        playerSpawnPosition = report.playerSpawnPosition;
        goalData = report.goal;
        return true;
    }

    private static void FillAvailablePreview(StoryStageBakeRequest request, StoryStageBakeReport report)
    {
        if (request == null)
        {
            return;
        }

        report.stageName = request.stageDefinition != null ? request.stageDefinition.name : string.Empty;

        if (!CanConvertWorldPosition(request))
        {
            return;
        }

        if (request.playerSpawn != null)
        {
            report.playerSpawnPosition = ConvertWorldToStoryPosition(request, request.playerSpawn.transform.position);
            report.hasPlayerSpawnPosition = true;
        }

        if (request.goal != null && request.goal.BoxCollider != null)
        {
            report.goal = BuildGoalData(request, request.goal);
            report.hasGoal = true;
        }
    }

    private static void ValidateRequest(StoryStageBakeRequest request, StoryStageBakeReport report)
    {
        if (request == null)
        {
            report.errors.Add("Story stage bake request is null.");
            return;
        }

        if (request.output == null)
        {
            report.errors.Add("StoryStageConfig output is not assigned.");
        }

        if (request.stageDefinition == null)
        {
            report.errors.Add("StageDefinition is not assigned.");
        }
        else if (request.stageDefinition.StageBakeData == null)
        {
            report.errors.Add("StageDefinition.StageBakeData is not assigned.");
        }

        if (request.stageRender == null)
        {
            report.errors.Add("StageRenderBinding is not assigned.");
        }
        else if (request.stageRender.Grid == null)
        {
            report.errors.Add("StageRenderBinding.Grid is not assigned.");
        }

        if (request.playerSpawn == null)
        {
            report.errors.Add("StorySpawnMarker is not assigned.");
        }

        if (request.goal == null)
        {
            report.errors.Add("StoryGoalMarker is not assigned.");
        }
        else if (request.goal.BoxCollider == null)
        {
            report.errors.Add("StoryGoalMarker.BoxCollider is not assigned.");
        }
    }

    private static StoryGoalData BuildGoalData(StoryStageBakeRequest request, StoryGoalMarker marker)
    {
        BoxCollider2D box = marker.BoxCollider;
        Vector2 goalPosition = ConvertWorldToStoryPosition(request, marker.transform.position);
        Vector2 boxCenter = ConvertWorldToStoryPosition(request, box.transform.TransformPoint(box.offset));
        Vector2 boxSize = ResolveColliderSize(request, box);

        return new StoryGoalData
        {
            position = goalPosition,
            colliderOffset = boxCenter - goalPosition,
            colliderSize = boxSize
        };
    }

    private static bool CanConvertWorldPosition(StoryStageBakeRequest request)
    {
        return request.stageDefinition != null
            && request.stageDefinition.StageBakeData != null
            && request.stageRender != null
            && request.stageRender.Grid != null;
    }

    private static Vector2 ResolveColliderSize(StoryStageBakeRequest request, BoxCollider2D box)
    {
        Vector2 halfSize = box.size * 0.5f;
        Vector3[] corners =
        {
            box.transform.TransformPoint(box.offset + new Vector2(-halfSize.x, -halfSize.y)),
            box.transform.TransformPoint(box.offset + new Vector2(-halfSize.x, halfSize.y)),
            box.transform.TransformPoint(box.offset + new Vector2(halfSize.x, -halfSize.y)),
            box.transform.TransformPoint(box.offset + new Vector2(halfSize.x, halfSize.y))
        };

        Vector2 min = ConvertWorldToStoryPosition(request, corners[0]);
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = ConvertWorldToStoryPosition(request, corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return max - min;
    }

    private static Vector2 ConvertWorldToStoryPosition(StoryStageBakeRequest request, Vector3 worldPosition)
    {
        Grid grid = request.stageRender.Grid;
        StageBakeData bakeData = request.stageDefinition.StageBakeData;
        Vector3 gridLocal = grid.transform.InverseTransformPoint(worldPosition);
        Vector2 offset = (Vector2)bakeData.StageOffsetPosition * bakeData.CellSize;
        return new Vector2(gridLocal.x, gridLocal.y) - offset;
    }
}
