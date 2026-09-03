using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Box = FurniturePlacementGeometry.Box;

/// <summary>Headless regression checks; also runnable from the Unity Tools menu.</summary>
public static class FurniturePlacementVerification
{
    private static readonly List<GameObject> objects = new List<GameObject>();
    private static int checks;

    [MenuItem("Tools/Furniture/Verify placement constraints")]
    public static void Run()
    {
        int result = 0;
        try
        {
            checks = 0;
            Geometry();
            WallAndRotation();
            Cleanup();
            VirtualFurniture();
            Cleanup();
            Appearance();
            Cleanup();
            InputOwnership();
            Cleanup();
            PrefabBindings();
            RoomSetupChoices();
            Cleanup();
            RealObjectPhysics();
            Cleanup();
            RotationDestinations();
            Debug.Log($"[PlacementVerification] PASS: {checks} assertions.");
        }
        catch (Exception error) { Debug.LogException(error); result = 1; }
        finally { Cleanup(); }
        if (Application.isBatchMode) EditorApplication.Exit(result);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("[PlacementVerification] " + message);
        checks++;
    }
    private static void Geometry()
    {
        Box a = new Box(Vector3.zero, Vector3.one * 0.5f, Quaternion.identity);
        Box wall = new Box(new Vector3(2, 0, 0), new Vector3(0.04f, 3, 3), Quaternion.identity);
        Check(FurniturePlacementGeometry.Sweep(a, wall, Vector3.right * 20, out float t), "Fast sweep missed a thin wall.");
        Check(Mathf.Abs(t - 0.073f) < 0.0001f, "Wrong first contact distance.");
        Check(!FurniturePlacementGeometry.Sweep(a, wall, Vector3.left * 20, out _), "Motion away from wall was blocked.");
        Check(!FurniturePlacementGeometry.Overlaps(a, wall), "Separated boxes reported overlapping.");
        Check(FurniturePlacementGeometry.Overlaps(a, a), "Initial overlap not detected.");
        Box rotated = new Box(new Vector3(0.9f, 0, 0), Vector3.one * 0.5f, Quaternion.Euler(0, 45, 0));
        Check(FurniturePlacementGeometry.Overlaps(a, rotated), "Rotated corners were missed.");
        Box diagonal = new Box(Vector3.one * 2, Vector3.one * 0.3f, Quaternion.Euler(20, 35, 15));
        Check(!FurniturePlacementGeometry.Overlaps(a, diagonal), "Separated rotated boxes overlap.");
    }

    private static BoxCollider Wall(float x)
    {
        GameObject go = new GameObject("Verification wall");
        objects.Add(go);
        go.transform.position = new Vector3(x, 1, 0);
        BoxCollider collider = go.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.08f, 4, 20);
        SceneAutoScanner.PlacementWalls.Add(collider);
        return collider;
    }
    private static FurniturePlacementController Furniture(Vector3 position, Vector3 size, out Transform visual)
    {
        GameObject root = new GameObject("Verification furniture");
        objects.Add(root);
        root.transform.position = position;
        root.AddComponent<Rigidbody>().isKinematic = true;
        root.AddComponent<FurnitureInteractionStateController>();
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.size = size;
        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(model.GetComponent<Collider>());
        model.transform.SetParent(root.transform, false);
        model.transform.localScale = size;
        // A translated and scaled visual exposes root/Visuals coordinate mistakes.
        model.transform.localPosition = new Vector3(0.1f, 0, 0);
        collider.center = model.transform.localPosition;
        visual = model.transform;
        FurnitureWallCollisionGuard guard = root.AddComponent<FurnitureWallCollisionGuard>();
        Check(guard.Configure(collider, visual), "Could not initialize a valid furniture pose.");
        return root.GetComponent<FurniturePlacementController>();
    }
    private static void WallAndRotation()
    {
        BoxCollider wall = Wall(2);
        FurniturePlacementController guard = Furniture(Vector3.zero, new Vector3(1, 1, 2.5f), out Transform visual);
        guard.RequestGrabbed(true);
        guard.RequestPose(new Pose(visual.position + Vector3.right * 20, visual.rotation));
        guard.ProcessFrame(1f / 90f);
        Check(visual.position.x < 1.46f && visual.position.x > 1.4f, "Fast drag did not stop at the near side of wall.");
        Vector3 stopped = visual.position;
        guard.RequestPose(new Pose(visual.position, Quaternion.Euler(0, 90, 0)));
        guard.ProcessFrame(1f / 90f);
        Check(Quaternion.Angle(visual.rotation, Quaternion.Euler(0, 90, 0)) < 0.1f, "Rotation changed the requested angle.");
        guard.RequestPose(new Pose(stopped - Vector3.right, visual.rotation));
        guard.ProcessFrame(1f / 90f);
        Check(visual.position.x < 0.5f, "Furniture could not be pulled away from a wall.");
        guard.RequestPose(new Pose(visual.position, Quaternion.Euler(0, 90, 0)));
        guard.ProcessFrame(1f / 90f);
        Check(Quaternion.Angle(visual.rotation, Quaternion.Euler(0, 90, 0)) < 0.1f, "Free-space rotation was blocked.");
        guard.RequestGrabbed(false);
        guard.RequestPose(new Pose(visual.position + Vector3.right * 20, visual.rotation));
        guard.ProcessFrame(1f / 90f);
        Check(visual.position.x < 0.71f, "Release/root correction crossed the wall.");
        Check(!FurniturePlacementGeometry.Overlaps(
            FurniturePlacementGeometry.FromBounds(new Bounds(Vector3.zero, Vector3.one), visual),
            FurniturePlacementGeometry.FromBounds(new Bounds(wall.center, wall.size), wall.transform)), "Final visual still intersects wall.");
        FurniturePlacementController spawned = Furniture(new Vector3(2, 0, 3), Vector3.one, out Transform spawnedVisual);
        Check(Mathf.Abs(spawnedVisual.position.x - 2) > 0.54f, "Spawn inside wall was not relocated.");
    }
    private static void VirtualFurniture()
    {
        Wall(20);
        FurniturePlacementController first = Furniture(Vector3.zero, Vector3.one, out Transform firstVisual);
        FurniturePlacementController second = Furniture(new Vector3(3, 0, 0), Vector3.one, out Transform secondVisual);
        first.RequestGrabbed(true);
        first.RequestPose(new Pose(firstVisual.position + Vector3.right * 8, firstVisual.rotation));
        first.ProcessFrame(1f / 90f);
        Check(firstVisual.position.x < secondVisual.position.x - 1, "Virtual furniture tunnelled through another model.");
        Check(Mathf.Abs(secondVisual.position.x - 3.1f) < 0.001f, "Blocking furniture was displaced.");
        second.RequestGrabbed(true);
        second.RequestPose(new Pose(secondVisual.position + Vector3.left * 8, secondVisual.rotation));
        second.ProcessFrame(1f / 90f);
        Check(secondVisual.position.x > firstVisual.position.x + 1, "Two grabbed models were allowed to overlap.");
        UnityEngine.Object.DestroyImmediate(second.gameObject);
        first.RequestPose(new Pose(firstVisual.position + Vector3.right * 5, firstVisual.rotation));
        first.ProcessFrame(1f / 90f);
        Check(firstVisual.position.x > 6, "Destroyed furniture remained an obstacle.");
    }
    private static void Appearance()
    {
        Wall(20);
        FurniturePlacementController guard = Furniture(Vector3.zero, Vector3.one, out Transform visual);
        Renderer renderer = visual.GetComponent<Renderer>();
        Material original = renderer.sharedMaterial;
        // Use the same boxes that RefreshRoom derives from MRUK furniture anchors.
        var field = typeof(FurnitureWallCollisionGuard).GetField("realFurniture", BindingFlags.NonPublic | BindingFlags.Instance);
        var real = (List<Box>)field.GetValue(guard.GetComponent<FurnitureWallCollisionGuard>());
        real.Add(new Box(new Vector3(2, 0, 0), Vector3.one, Quaternion.identity));
        guard.RequestGrabbed(true);
        guard.RequestPose(new Pose(new Vector3(2, 0, 0), visual.rotation));
        guard.ProcessFrame(1f / 90f);
        Check(Mathf.Abs(visual.position.x - 2) < 0.01f, "Scanned furniture incorrectly blocked movement.");
        Check(renderer.sharedMaterial != original, "Overlap did not change the material.");
        Check(renderer.sharedMaterial.GetColor("_BaseColor").a < 0.5f, "Overlap is not translucent.");
        Check(!ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader), "Fade shader has compilation errors.");
        guard.RequestPose(new Pose(Vector3.zero, visual.rotation));
        guard.ProcessFrame(1f / 90f);
        Check(renderer.sharedMaterial == original, "Original material did not return after leaving overlap.");
    }
    private static void Cleanup()
    {
        foreach (GameObject go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        objects.Clear();
        SceneAutoScanner.PlacementWalls.RemoveWhere(wall => wall == null);
    }

    private static void InputOwnership()
    {
        Wall(2);
        var controller = Furniture(Vector3.zero, Vector3.one, out Transform visual);
        Vector3 original = visual.position;
        Vector3 originalLocal = visual.localPosition;
        controller.RequestGrabbed(true);
        controller.GrabInputTarget.position = Vector3.right * 20;
        Check(visual.position == original, "SDK input target moved the visible model before validation.");
        controller.ProcessFrame(1f / 90f);
        Check(visual.position.x < 1.46f, "SDK input bypassed the placement controller.");
        Check(visual.localPosition == originalLocal, "Grab changed the model's local pose.");
        BoxCollider rootBox = controller.GetComponent<BoxCollider>();
        Check(Vector3.Distance(rootBox.transform.TransformPoint(rootBox.center), visual.position) < 0.001f,
            "Rendered model and interaction collider diverged during grab.");
        Vector3 accepted = visual.position;
        controller.RequestGrabbed(false);
        Check(visual.position == accepted, "Release event applied a pose before validation.");
        controller.ProcessFrame(1f / 90f);
        Check(visual.position.x < 1.46f && visual.localPosition == originalLocal, "Release caused a reparent jump.");
        controller.RequestRotation(90);
        Quaternion before = visual.rotation;
        Check(visual.rotation == before, "Rotation request applied immediately.");
        controller.ProcessFrame(1f / 90f);
        Check(visual.position.x < 1.46f, "Rotation button bypassed collision validation.");
        controller.RequestPose(new Pose(Vector3.zero, Quaternion.identity));
        controller.RequestPose(new Pose(new Vector3(-1, 0, 0), Quaternion.identity));
        Check(visual.position == accepted, "Queued input wrote to the model.");
        controller.ProcessFrame(1f / 90f);
        Check(Vector3.Distance(visual.position, new Vector3(-1, 0, 0)) < 0.001f, "Latest pose request was not applied.");
    }

    private static void PrefabBindings()
    {
        Wall(20);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/template.prefab");
        Check(prefab != null, "The production furniture prefab is missing.");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        objects.Add(root);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;
        Transform visual = root.transform.Find("Visuals");
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.transform.SetParent(visual, false);
        BoxCollider box = root.GetComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = Vector3.one;
        var validator = root.AddComponent<FurnitureWallCollisionGuard>();
        Check(validator.Configure(box, visual), "Production prefab could not initialize placement.");
        var controller = root.GetComponent<FurniturePlacementController>();
        var grabs = root.GetComponentsInChildren<Oculus.Interaction.Grabbable>(true);
        Check(grabs.Length > 0, "Production Meta grab binding was not tested.");
        foreach (var grab in grabs)
            Check(grab.Transform == controller.GrabInputTarget && grab.Transform != root.transform,
                "Meta SDK still writes the furniture root directly.");
        Check(controller.GrabInputTarget.GetComponentInChildren<Renderer>() == null,
            "Raw grab input unexpectedly has visible geometry.");
        controller.RequestGrabbed(true);
        controller.GrabInputTarget.position = Vector3.right * 40;
        Check(root.transform.position == Vector3.zero, "Production input modified the actual furniture before validation.");
        controller.ProcessFrame(1f / 90f);
        Check(root.transform.position.x < 19.5f, "Production prefab bypassed the wall constraint.");
    }

    private static void RoomSetupChoices()
    {
        Check(SceneAutoScanner.ChooseRoomSetup(false, false, false, false) == SceneAutoScanner.RoomSetupAction.None,
            "Missing room data automatically entered a setup path.");
        Check(SceneAutoScanner.ChooseRoomSetup(false, true, false, false) == SceneAutoScanner.RoomSetupAction.None,
            "Unavailable room was accepted.");
        Check(SceneAutoScanner.ChooseRoomSetup(false, false, true, false) == SceneAutoScanner.RoomSetupAction.Scan,
            "No-room scan choice did not request the official scan flow.");
        Check(SceneAutoScanner.ChooseRoomSetup(true, false, true, false) == SceneAutoScanner.RoomSetupAction.Scan,
            "Saved-room rescan choice was ignored.");
        Check(SceneAutoScanner.ChooseRoomSetup(true, true, false, false) == SceneAutoScanner.RoomSetupAction.UseSaved,
            "Saved-room choice failed.");
        Check(SceneAutoScanner.ChooseRoomSetup(false, false, false, true) == SceneAutoScanner.RoomSetupAction.Manual,
            "Explicit manual setup choice failed.");
        string missing = SceneAutoScanner.BuildRoomChoiceText(false, "NoScenePermission", "not granted", 0, 0, 0);
        Check(missing.Contains("NoScenePermission") && missing.Contains("not granted"), "Permission failure is hidden from the user.");
        Check(!missing.Contains("A: Use") && missing.Contains("B: Scan"), "Missing-room menu offers an invalid saved room.");
        string loaded = SceneAutoScanner.BuildRoomChoiceText(true, "Success", "granted", 4, 1, 1);
        Check(loaded.Contains("WALL: 4") && !loaded.Contains("FLOOR:") && !loaded.Contains("CEILING:"),
            "Room menu must show only the wall count.");
    }

    private static void RealObjectPhysics()
    {
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            Check(scene.GetPhysicsScene() != Physics.defaultPhysicsScene, "Physics test must use an isolated preview scene.");
            BoxCollider wall = Wall(20);
            SceneManager.MoveGameObjectToScene(wall.gameObject, scene);
            var controller = Furniture(new Vector3(0, 3, 0), new Vector3(1, 1, 3), out Transform movingVisual);
            SceneManager.MoveGameObjectToScene(controller.gameObject, scene);
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(platform);
            SceneManager.MoveGameObjectToScene(platform, scene);
            platform.transform.position = new Vector3(0, 1.5f, 0);
            platform.transform.localScale = new Vector3(6, 0.2f, 6);
            Collider unlabelled = platform.GetComponent<Collider>();
            GameObject floorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            objects.Add(floorObject);
            SceneManager.MoveGameObjectToScene(floorObject, scene);
            floorObject.transform.position = new Vector3(0, -0.1f, 0);
            floorObject.transform.localScale = new Vector3(8, 0.2f, 8);
            Collider floor = floorObject.GetComponent<Collider>();
            SceneAutoScanner.PlacementFloors.Add(floor);
            var guard = controller.GetComponent<FurnitureWallCollisionGuard>();
            var body = controller.GetComponent<Rigidbody>();
            var child = new GameObject("Compound furniture collider");
            child.transform.SetParent(controller.transform, false);
            child.AddComponent<BoxCollider>().size = Vector3.one * 0.5f;
            controller.GetComponent<FurnitureInteractionStateController>().SetState(FurnitureInteractionState.Placed);
            body.constraints = RigidbodyConstraints.FreezeRotation;
            Check(!FurnitureWallCollisionGuard.BlocksFurniturePhysics(unlabelled), "Unlabelled geometry blocks furniture.");
            Check(FurnitureWallCollisionGuard.BlocksFurniturePhysics(floor), "Manual floor is not a support surface.");
            Check(FurnitureWallCollisionGuard.BlocksFurniturePhysics(wall), "Wall physics disabled.");
            Check(FurnitureWallCollisionGuard.BlocksFurniturePhysics(body.GetComponent<Collider>()), "Virtual furniture physics disabled.");
            Physics.SyncTransforms();
            guard.RefreshPhysicsContacts();
            for (int i = 0; i < 150; i++)
            {
                scene.GetPhysicsScene().Simulate(0.02f);
            }
            Check(body.position.y > 0.45f && body.position.y < 0.56f,
                "Furniture did not pass through the unlabelled rack and land on the floor: " + body.position.y);
            Check((body.GetComponent<Collider>().excludeLayers.value & (1 << unlabelled.gameObject.layer)) != 0,
                "Raw scan layer was not excluded.");
            Check(child.GetComponent<Collider>().excludeLayers == body.GetComponent<Collider>().excludeLayers,
                "Compound furniture collider can still touch scanned objects.");
            var neighbour = Furniture(new Vector3(1.2f, 0.5f, 0), Vector3.one, out Transform neighbourVisual);
            SceneManager.MoveGameObjectToScene(neighbour.gameObject, scene);
            neighbour.GetComponent<FurnitureInteractionStateController>().SetState(FurnitureInteractionState.Placed);
            Rigidbody neighbourBody = neighbour.GetComponent<Rigidbody>();
            neighbourBody.useGravity = false;
            neighbourBody.constraints = RigidbodyConstraints.FreezeRotation;
            Vector3 neighbourStart = neighbourBody.position;
            controller.RequestGrabbed(true);
            controller.RequestPose(new Pose(movingVisual.position, Quaternion.Euler(0, 90, 0)), true);
            controller.ProcessFrame(0.01f);
            guard.RefreshPhysicsContacts();
            neighbour.GetComponent<FurnitureWallCollisionGuard>().RefreshPhysicsContacts();
            Physics.SyncTransforms();
            for (int i = 0; i < 20; i++) scene.GetPhysicsScene().Simulate(0.02f);
            Check(Vector3.Distance(neighbourBody.position, neighbourStart) < 0.01f,
                "Rotation preview pushed another virtual furniture item.");
            controller.RequestGrabbed(false);
            controller.RequestPose(new Pose(movingVisual.position, Quaternion.Euler(0, 90, 0)));
            controller.ProcessFrame(0.01f);
            Check(!FurniturePlacementGeometry.Overlaps(
                FurniturePlacementGeometry.FromBounds(new Bounds(Vector3.zero, Vector3.one), movingVisual),
                FurniturePlacementGeometry.FromBounds(new Bounds(Vector3.zero, Vector3.one), neighbourVisual)),
                "Completed rotation overlaps virtual furniture.");
            SceneAutoScanner.PlacementFloors.Remove(floor);
        }
        finally
        {
            Cleanup();
            SceneAutoScanner.PlacementFloors.RemoveWhere(c => c == null);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void RotationDestinations()
    {
        BoxCollider wall = Wall(1.8f);
        var controller = Furniture(new Vector3(1, 0, 0), new Vector3(1, 1, 3), out Transform visual);
        controller.RequestGrabbed(true);
        Vector3 pivot = visual.position;
        controller.RequestPose(new Pose(pivot, Quaternion.Euler(0, 180, 0)));
        controller.ProcessFrame(0.01f);
        Check(Quaternion.Angle(visual.rotation, Quaternion.Euler(0, 180, 0)) < 0.01f,
            "A valid final angle was blocked by an intermediate corner.");
        controller.RequestPose(new Pose(pivot, Quaternion.Euler(0, 90, 0)), rotationInProgress: true);
        controller.ProcessFrame(0.01f);
        Check(Quaternion.Angle(visual.rotation, Quaternion.Euler(0, 90, 0)) < 0.01f,
            "Live rotation preview stopped at the wall.");
        controller.RequestPose(new Pose(pivot, Quaternion.Euler(0, 90, 0)));
        controller.ProcessFrame(0.01f);
        Check(!FurniturePlacementGeometry.Overlaps(
            FurniturePlacementGeometry.FromBounds(new Bounds(Vector3.zero, Vector3.one), visual),
            FurniturePlacementGeometry.FromBounds(new Bounds(wall.center, wall.size), wall.transform)),
            "Completed rotation was not adjusted away from the wall.");
        Check(Quaternion.Angle(visual.rotation, Quaternion.Euler(0, 90, 0)) < 90f,
            "Invalid destination did not choose a nearby legal angle.");
        controller.GrabInputTarget.rotation = Quaternion.Euler(0, 90, 0);
        controller.ProcessFrame(0.01f);
        Check(Quaternion.Angle(visual.rotation, Quaternion.Euler(0, 90, 0)) < 0.01f,
            "Controller rotation preview was not enabled.");
        for (int i = 0; i < 25; i++) controller.ProcessFrame(0.01f);
        Check(Quaternion.Angle(visual.rotation, Quaternion.Euler(0, 90, 0)) < 0.01f,
            "Idle rotation input changed the requested angle.");
        Vector3 beforePull = visual.position;
        controller.GrabInputTarget.position += Vector3.back * 0.4f;
        controller.ProcessFrame(0.01f);
        Check(visual.position.z < beforePull.z - 0.39f,
            "Pulling parallel to a wall failed to follow the controller.");
        Check(Quaternion.Angle(visual.rotation, Quaternion.Euler(0, 90, 0)) < 0.01f,
            "Pulling the controller rotated the model.");
        Check(FurniturePlacementController.FilterThumbstick(new Vector2(0.12f, 0.8f)).x == 0f,
            "Forward stick input also triggers rotation.");
        Check(FurniturePlacementController.FilterThumbstick(new Vector2(0.8f, 0.12f)).y == 0f,
            "Rotation stick input also triggers translation.");
        controller.GrabInputTarget.rotation = Quaternion.Euler(0, 89, 0);
        controller.ProcessFrame(0.01f);
        controller.RequestGrabbed(false);
        controller.ProcessFrame(0.01f);
        Check(!FurniturePlacementGeometry.Overlaps(
            FurniturePlacementGeometry.FromBounds(new Bounds(Vector3.zero, Vector3.one), visual),
            FurniturePlacementGeometry.FromBounds(new Bounds(wall.center, wall.size), wall.transform)),
            "Release left an invalid preview against the wall.");
    }

}



