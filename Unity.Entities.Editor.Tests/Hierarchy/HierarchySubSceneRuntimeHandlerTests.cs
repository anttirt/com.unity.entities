using NUnit.Framework;
using Unity.Scenes;
using Unity.Scenes.Editor.Tests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity.Entities.Editor.Tests
{
    public class HierarchySubSceneRuntimeHandlerTests
    {
        const string k_LongSubSceneName =
            "ALongSubSceneNameThatExceedsTheSixtyOneByteFixedStringSixtyFourBytesCapWithSomeExtraPaddingChars12345";

        Unity.Hierarchy.Hierarchy m_Hierarchy;
        HierarchySubSceneRuntimeHandler m_SubSceneHandler;
        World m_PreviousWorld;
        World m_World;
        GameObject m_SubSceneHost;
        string m_TempSceneAssetPath;
        TestWithTempAssets m_TempAssets;

        [SetUp]
        public void SetUp()
        {
            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            m_World = World.DefaultGameObjectInjectionWorld = new World("Test World");
            m_Hierarchy = new Unity.Hierarchy.Hierarchy();
            m_SubSceneHandler = m_Hierarchy.GetOrCreateNodeTypeHandler<HierarchySubSceneRuntimeHandler>();
            m_Hierarchy.GetOrCreateNodeTypeHandler<HierarchyWorldHandler>();
            m_TempAssets.SetUp();

            UpdateHierarchy(m_Hierarchy);
        }

        [TearDown]
        public void TearDown()
        {
            if (m_SubSceneHost != null)
                Object.DestroyImmediate(m_SubSceneHost);
            m_SubSceneHandler = null;
            m_Hierarchy?.Dispose();
            m_Hierarchy = null;
            m_World?.Dispose();
            m_World = null;
            World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
            if (!string.IsNullOrEmpty(m_TempSceneAssetPath))
                AssetDatabase.DeleteAsset(m_TempSceneAssetPath);
            m_TempAssets.TearDown();
        }

        static void UpdateHierarchy(Unity.Hierarchy.Hierarchy hierarchy)
        {
            int count = 100;
            while (hierarchy.UpdateNeeded && count-- > 0)
                hierarchy.Update();
            Assert.IsFalse(hierarchy.UpdateNeeded);
        }

        SubScene CreateSubScene(string sceneName)
        {
            m_TempSceneAssetPath = $"Assets/{sceneName}.unity";
            AssetDatabase.DeleteAsset(m_TempSceneAssetPath);
            var tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(tempScene, m_TempSceneAssetPath);
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(m_TempSceneAssetPath);
            Assume.That(sceneAsset, Is.Not.Null, "Failed to create temp SceneAsset for test.");

            m_SubSceneHost = new GameObject("test-subscene-host");
            var subScene = m_SubSceneHost.AddComponent<SubScene>();
            subScene.SceneAsset = sceneAsset;
            return subScene;
        }

        [Test]
        public void UpdateHierarchySystem_DoesNotThrow_WhenSubSceneNameExceedsFixedString64BytesCap()
        {
            var subScene = CreateSubScene(k_LongSubSceneName);
            var entity = m_World.EntityManager.CreateEntity();
            #pragma warning disable 0618 // managed API obsolete; internal/test caller still needs it.
            m_World.EntityManager.AddComponentObject(entity, subScene);
            #pragma warning restore 0618

            var hierarchySystem = m_World.GetOrCreateSystem<UpdateHierarchySystem>();

            Assert.DoesNotThrow(() =>
            {
                hierarchySystem.Update(m_World.Unmanaged);
                UpdateHierarchy(m_Hierarchy);
            });
        }

        [Test]
        public void CreateSubSceneNode_PreservesFullName_WhenSceneNameExceedsFixedString64BytesCap()
        {
            m_World.GetOrCreateSystem<SceneSystem>();

            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(
                ref m_TempAssets, k_LongSubSceneName, false, null);

            var sceneEntity = SceneSystem.GetSceneEntity(m_World.Unmanaged, subScene.SceneGUID);
            Assume.That(sceneEntity, Is.Not.EqualTo(Entity.Null),
                "SceneSystem did not produce a scene entity for the SubScene.");
            Assume.That(m_World.EntityManager.HasComponent<SubScene>(sceneEntity), Is.True,
                "SubScene MB should have been attached to the scene entity by SubScene.OnEnable.");

            var hierarchySystem = m_World.GetOrCreateSystem<UpdateHierarchySystem>();

            hierarchySystem.Update(m_World.Unmanaged);
            UpdateHierarchy(m_Hierarchy);

            Assert.That(m_SubSceneHandler.TryGetSubSceneNode(sceneEntity, out var node), Is.True,
                "Subscene node should have been created.");
            Assert.That(m_Hierarchy.GetName(node), Is.EqualTo(k_LongSubSceneName));
        }

        [Test]
        public void SubSceneNode_IsRemoved_WhenDestroyedEntityIsKeptAliveByCleanupComponent()
        {
            m_World.GetOrCreateSystem<SceneSystem>();

            var subScene = SubSceneTestsHelper.CreateSubSceneFromObjects(
                ref m_TempAssets, "cleanup-component-subscene", false, null);

            var sceneEntity = SceneSystem.GetSceneEntity(m_World.Unmanaged, subScene.SceneGUID);
            Assume.That(sceneEntity, Is.Not.EqualTo(Entity.Null),
                "SceneSystem did not produce a scene entity for the SubScene.");
            Assume.That(m_World.EntityManager.HasComponent<SubScene>(sceneEntity), Is.True,
                "SubScene MB should have been attached to the scene entity by SubScene.OnEnable.");

            var hierarchySystem = m_World.GetOrCreateSystem<UpdateHierarchySystem>();
            var cleanupSystem = m_World.GetOrCreateSystemManaged<SubSceneNodeCleanupSystem>();

            hierarchySystem.Update(m_World.Unmanaged);
            cleanupSystem.Update();
            UpdateHierarchy(m_Hierarchy);

            Assert.That(m_SubSceneHandler.TryGetSubSceneNode(sceneEntity, out _), Is.True,
                "Subscene node should have been created before the destroy step.");

            // Simulate the live conversion patcher tagging the scene entity with a cleanup
            // component, then unloading the scene. DestroyEntity strips SubScene but the
            // ICleanupComponentData keeps the entity handle alive, so
            // UpdateHierarchySystem's EntityDiffer never reports it as destroyed.
            m_World.EntityManager.AddComponentData(sceneEntity, new LiveConversionPatcher.LiveConvertedSceneCleanup());
            m_World.EntityManager.DestroyEntity(sceneEntity);

            Assume.That(m_World.EntityManager.Exists(sceneEntity), Is.True,
                "Entity should still exist because of the cleanup component; test premise is broken otherwise.");

            hierarchySystem.Update(m_World.Unmanaged);
            cleanupSystem.Update();
            UpdateHierarchy(m_Hierarchy);

            Assert.That(m_SubSceneHandler.TryGetSubSceneNode(sceneEntity, out _), Is.False,
                "Subscene node should have been removed once the entity left the subscene query.");
        }
    }
}
