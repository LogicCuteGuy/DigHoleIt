using LogicCuteGuy.DigHoleIt.Editor;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;

namespace LogicCuteGuy.DigHoleIt.Udon.Editor
{
    /// <summary>VRChat demo: the standard demo terrain and zone plus a VRCWorld spawn and three shovels (dig / add / paint).</summary>
    internal static class DigVrcDemoBuilder
    {
        private const string VrcWorldPrefab = "Packages/com.vrchat.worlds/Samples/UdonExampleScene/Prefabs/VRCWorld.prefab";

        [MenuItem("Tools/DigHoleIt/Create VRChat Demo Scene")]
        private static void Create()
        {
            DigZone zone = DigDemoBuilder.Build("DigHoleItVRChatDemo");
            if (zone == null) return;

            DigZoneRuntime rt = DigUdonBridge.AddRuntime(zone);
            Bounds b = zone.WorldBounds;
            Vector3 spawn = new Vector3(b.min.x - 3f, 0f, b.center.z);
            spawn.y = zone.terrain.SampleHeight(spawn) + zone.terrain.transform.position.y + 0.1f;

            var worldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VrcWorldPrefab);
            if (worldPrefab != null)
            {
                var world = (GameObject)PrefabUtility.InstantiatePrefab(worldPrefab);
                world.transform.position = spawn;
                world.transform.rotation = Quaternion.LookRotation(Vector3.right);
            }

            MakeShovel("Shovel (Dig)", rt, DigTool.ModeDig, new Color(0.9f, 0.45f, 0.2f), spawn + new Vector3(1.2f, 1f, -0.6f));
            MakeShovel("Shovel (Add)", rt, DigTool.ModeAdd, new Color(0.3f, 0.8f, 0.35f), spawn + new Vector3(1.2f, 1f, 0.6f));
            MakeShovel("Shovel (Paint Rock)", rt, DigTool.ModePaint, new Color(0.95f, 0.8f, 0.2f), spawn + new Vector3(1.2f, 1f, 1.8f));

            Object.DestroyImmediate(GameObject.Find("Main Camera"));
            DigDemoBuilder.Save(zone);
        }

        private static void MakeShovel(string name, DigZoneRuntime zone, int mode, Color color, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = new Vector3(0.08f, 0.08f, 0.9f);
            var mat = new Material(Shader.Find("Standard")) { color = color };
            AssetDatabase.CreateAsset(mat, AssetDatabase.GenerateUniqueAssetPath($"{DigDemoBuilder.Folder}/{name}.mat"));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var pickup = go.AddComponent<VRCPickup>();
            pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
            pickup.UseText = mode == DigTool.ModeDig ? "Dig" : mode == DigTool.ModeAdd ? "Add soil" : "Paint";
            pickup.InteractionText = name;

            var tip = new GameObject("Tip").transform;
            tip.SetParent(go.transform, false);
            tip.localPosition = new Vector3(0f, 0f, 0.5f);

            DigTool tool = go.AddUdonSharpComponent<DigTool>();
            tool.zones = new[] { zone };
            tool.tip = tip;
            tool.mode = mode;
            tool.paintLayer = 2; // demo terrain layer 1: Rock
            UdonSharpEditorUtility.CopyProxyToUdon(tool);
        }
    }
}
