using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class StorySelectSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Scene Story Select.unity";
    private const string StagePreviewPath = "Assets/_Project/Prefab/Story/StagePreview.prefab";
    private const string CharacterPreviewPath = "Assets/_Project/Prefab/Story/CharacterPreview.prefab";
    private const string SkillPreviewPath = "Assets/_Project/Prefab/Story/SkillPreview.prefab";

    private const string StoryStageCatalogPath = "Assets/_Project/Data/Story/Story Stage Catalog.asset";
    private const string CharacterCatalogPath = "Assets/_Project/Data/Character/Character Catalog.asset";
    private const string SkillCatalogPath = "Assets/_Project/Data/Skill/Skill Catalog.asset";
    private const int TemporaryCharacterSlotCount = 4;
    private const int TemporarySkillSlotCount = 6;

    [MenuItem("Tag Survival/Story/Bind Story Select Scene")]
    public static void BindScene()
    {
        PreparePreviewPrefab<StoryStageSlotView>(StagePreviewPath);
        PreparePreviewPrefab<StoryCharacterSlotView>(CharacterPreviewPath);
        PreparePreviewPrefab<StorySkillSlotView>(SkillPreviewPath);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        StoryStageGridView stageGrid = BindStageGrid(scene);
        StoryCharacterGridView characterGrid = BindCharacterGrid(scene);
        StorySkillGridView skillGrid = BindSkillGrid(scene);
        BindController(scene, stageGrid, characterGrid, skillGrid);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static void Rebuild()
    {
        BindScene();
    }

    private static StoryStageGridView BindStageGrid(Scene scene)
    {
        GameObject gridObject = RequireSceneObject(scene, "Stage Grid");
        Transform content = RequireChild(gridObject.transform, "Content");
        ToggleGroup toggleGroup = EnsureComponent<ToggleGroup>(content.gameObject);
        ConfigureGridLayout(content.gameObject, 5, 0, new Vector2(16f, 16f), true);
        StoryStageGridView gridView = EnsureOnlyGridView<StoryStageGridView>(gridObject);
        List<StageSlotData> data = ReadStageSlots();
        StoryStageSlotView[] slots = SyncSlots(
            content,
            StagePreviewPath,
            data.Count,
            "StagePreview",
            EnsureStageSlot);

        for (int i = 0; i < slots.Length; i++)
        {
            StageSlotData slotData = data[i];
            slots[i].Configure(slotData.config, slotData.displayName, slotData.locked);
            MarkDirty(slots[i]);
        }

        gridView.Configure(slots, toggleGroup);
        MarkDirty(gridView);
        return gridView;
    }

    private static StoryCharacterGridView BindCharacterGrid(Scene scene)
    {
        GameObject gridObject = RequireSceneObject(scene, "Character Grid");
        Transform content = RequireChild(gridObject.transform, "Area Character");
        ToggleGroup toggleGroup = EnsureComponent<ToggleGroup>(content.gameObject);
        ConfigureGridLayout(content.gameObject, 2, 2, new Vector2(14f, 14f), false);
        StoryCharacterGridView gridView = EnsureOnlyGridView<StoryCharacterGridView>(gridObject);
        List<CharacterDefinition> data = ReadCharacterSlots();
        StoryCharacterSlotView[] slots = SyncSlots(
            content,
            CharacterPreviewPath,
            TemporaryCharacterSlotCount,
            "CharacterPreview",
            EnsureCharacterSlot);

        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].Configure(i < data.Count ? data[i] : null);
            MarkDirty(slots[i]);
        }

        gridView.Configure(slots, toggleGroup);
        MarkDirty(gridView);
        return gridView;
    }

    private static StorySkillGridView BindSkillGrid(Scene scene)
    {
        GameObject gridObject = RequireSceneObject(scene, "Skill Grid");
        Transform content = RequireChild(gridObject.transform, "Area Skill");
        ToggleGroup toggleGroup = EnsureComponent<ToggleGroup>(content.gameObject);
        ConfigureGridLayout(content.gameObject, 2, 3, new Vector2(14f, 14f), false);
        StorySkillGridView gridView = EnsureOnlyGridView<StorySkillGridView>(gridObject);
        List<SkillDefinition> data = ReadSkillSlots();
        StorySkillSlotView[] slots = SyncSlots(
            content,
            SkillPreviewPath,
            TemporarySkillSlotCount,
            "SkillPreview",
            EnsureSkillSlot);

        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].Configure(i < data.Count ? data[i] : null);
            MarkDirty(slots[i]);
        }

        gridView.Configure(slots, toggleGroup);
        MarkDirty(gridView);
        return gridView;
    }

    private static void BindController(
        Scene scene,
        StoryStageGridView stageGrid,
        StoryCharacterGridView characterGrid,
        StorySkillGridView skillGrid)
    {
        GameObject canvasObject = RequireSceneObject(scene, "Story Select Canvas");
        StorySelectSceneController controller = EnsureComponent<StorySelectSceneController>(canvasObject);
        Button startButton = FindSceneObject(scene, "Button Start")?.GetComponent<Button>();
        TMP_InputField nicknameInput = FindSceneObject(scene, "Input Nickname")?.GetComponent<TMP_InputField>();
        TMP_Text statusText = FindSceneObject(scene, "Status")?.GetComponent<TMP_Text>();

        controller.Configure(stageGrid, characterGrid, skillGrid, nicknameInput, startButton, statusText);
        MarkDirty(controller);

        StorySelectBootstrap bootstrap = Object.FindFirstObjectByType<StorySelectBootstrap>(FindObjectsInactive.Include);
        if (bootstrap != null)
        {
            SerializedObject serializedObject = new(bootstrap);
            SerializedProperty controllerProperty = serializedObject.FindProperty("controller");
            if (controllerProperty != null)
            {
                controllerProperty.objectReferenceValue = controller;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            MarkDirty(bootstrap);
        }
    }

    private static TSlot[] SyncSlots<TSlot>(
        Transform content,
        string prefabPath,
        int count,
        string slotName,
        System.Func<GameObject, TSlot> ensureSlot)
        where TSlot : Component
    {
        TrimExtraChildren(content, count);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            throw new MissingReferenceException($"Story preview prefab is missing: {prefabPath}");
        }

        TSlot[] slots = new TSlot[count];
        ToggleGroup toggleGroup = content.GetComponent<ToggleGroup>();
        for (int i = 0; i < count; i++)
        {
            GameObject slotObject;
            if (i < content.childCount)
            {
                slotObject = content.GetChild(i).gameObject;
            }
            else
            {
                slotObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, content);
            }

            slotObject.name = $"{slotName} {i + 1}";
            StoryRadioSlotView radioSlot = EnsureComponent<StoryRadioSlotView>(slotObject);
            if (radioSlot.Toggle != null)
            {
                radioSlot.Toggle.group = toggleGroup;
            }

            slots[i] = ensureSlot(slotObject);
            MarkDirty(slotObject);
        }

        return slots;
    }

    private static void ConfigureGridLayout(
        GameObject contentObject,
        int columns,
        int rows,
        Vector2 spacing,
        bool keepSquareCells)
    {
        StoryGridLayoutFitter fitter = EnsureComponent<StoryGridLayoutFitter>(contentObject);
        fitter.Configure(columns, rows, spacing, new RectOffset(), keepSquareCells);
        MarkDirty(fitter);
    }

    private static StoryStageSlotView EnsureStageSlot(GameObject slotObject)
    {
        return EnsureComponent<StoryStageSlotView>(slotObject);
    }

    private static StoryCharacterSlotView EnsureCharacterSlot(GameObject slotObject)
    {
        return EnsureComponent<StoryCharacterSlotView>(slotObject);
    }

    private static StorySkillSlotView EnsureSkillSlot(GameObject slotObject)
    {
        return EnsureComponent<StorySkillSlotView>(slotObject);
    }

    private static void PreparePreviewPrefab<TSlot>(string prefabPath)
        where TSlot : Component
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            EnsureComponent<TSlot>(root);

            MarkDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static TGrid EnsureOnlyGridView<TGrid>(GameObject gridObject)
        where TGrid : Component
    {
        RemoveWrongGridView<StoryStageGridView, TGrid>(gridObject);
        RemoveWrongGridView<StoryCharacterGridView, TGrid>(gridObject);
        RemoveWrongGridView<StorySkillGridView, TGrid>(gridObject);
        return EnsureComponent<TGrid>(gridObject);
    }

    private static void RemoveWrongGridView<TExisting, TExpected>(GameObject gridObject)
        where TExisting : Component
        where TExpected : Component
    {
        if (typeof(TExisting) == typeof(TExpected))
        {
            return;
        }

        TExisting existing = gridObject.GetComponent<TExisting>();
        if (existing != null)
        {
            Object.DestroyImmediate(existing, true);
        }
    }

    private static T EnsureComponent<T>(GameObject gameObject)
        where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        return component;
    }

    private static void TrimExtraChildren(Transform content, int count)
    {
        for (int i = content.childCount - 1; i >= count; i--)
        {
            Object.DestroyImmediate(content.GetChild(i).gameObject);
        }
    }

    private static GameObject RequireSceneObject(Scene scene, string objectName)
    {
        GameObject gameObject = FindSceneObject(scene, objectName);
        if (gameObject == null)
        {
            throw new MissingReferenceException($"Story Select scene object is missing: {objectName}");
        }

        return gameObject;
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject result = FindInChildren(roots[i].transform, objectName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static GameObject FindInChildren(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent.gameObject;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            GameObject result = FindInChildren(parent.GetChild(i), objectName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static Transform RequireChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child == null)
        {
            throw new MissingReferenceException($"Story Select child object is missing: {parent.name}/{childName}");
        }

        return child;
    }

    private static List<StageSlotData> ReadStageSlots()
    {
        List<StageSlotData> data = new();
        StoryStageCatalog catalog = AssetDatabase.LoadAssetAtPath<StoryStageCatalog>(StoryStageCatalogPath);
        if (catalog == null)
        {
            return data;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.TryGetByIndex(i, out StoryStageCatalogEntry entry) && entry.StageConfig != null)
            {
                data.Add(new StageSlotData(entry.StageConfig, entry.DisplayName, !entry.IsUnlocked()));
            }
        }

        return data;
    }

    private static List<CharacterDefinition> ReadCharacterSlots()
    {
        List<CharacterDefinition> data = new();
        CharacterCatalog catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CharacterCatalogPath);
        if (catalog == null)
        {
            return data;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.TryGetByIndex(i, out CharacterDefinition definition) && definition != null)
            {
                data.Add(definition);
            }
        }

        return data;
    }

    private static List<SkillDefinition> ReadSkillSlots()
    {
        List<SkillDefinition> data = new();
        SkillCatalog catalog = AssetDatabase.LoadAssetAtPath<SkillCatalog>(SkillCatalogPath);
        if (catalog == null)
        {
            return data;
        }

        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.TryGetByIndex(i, out SkillDefinition definition)
                && definition != null
                && catalog.TryGetPlayable(definition.SkillId, out _, out _))
            {
                data.Add(definition);
            }
        }

        return data;
    }

    private static void MarkDirty(Object target)
    {
        if (target != null)
        {
            EditorUtility.SetDirty(target);
        }
    }

    private readonly struct StageSlotData
    {
        public readonly StoryStageConfig config;
        public readonly string displayName;
        public readonly bool locked;

        public StageSlotData(StoryStageConfig config, string displayName, bool locked)
        {
            this.config = config;
            this.displayName = displayName;
            this.locked = locked;
        }
    }
}
