using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

public class BuildingUi : MonoBehaviour
{

    public Building[] AllBuilding;

    private bool isChoosen = false;
    private GameObject chosenGameObject;
    private Building chosenBuilding;
    private int chosenIndex;

    private EntityManager entityManager;
    private EntityQuery entityQuery;

    [System.Serializable]
    public struct Building
    {
        public float Cost;
        public GameObject prefab;
    }

    void Awake()
    {
        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        entityQuery = entityManager.CreateEntityQuery(typeof(BuildPrefab));
    }

    private void OnEnable()
    {
        var input = InputProvider.Instance;
        if (input == null) return;
        input.OnPlacePressed += HandlePlace;
        input.OnCancelPressed += HandleCancel;
    }

    private void OnDisable()
    {
        var input = InputProvider.Instance;
        if (input == null) return;
        input.OnPlacePressed -= HandlePlace;
        input.OnCancelPressed -= HandleCancel;
    }

    void Update()
    {
        if (!isChoosen)
            return;

        Vector3 world = InputProvider.Instance.GetWorldPointerPosition();
        world.z = chosenGameObject.transform.position.z;
        chosenGameObject.transform.position = world;
    }

    // Building selection from UI buttons.

    public void ChooseBuilding(Building building)
    {
        UnChooseBuilding();
        chosenBuilding = building;
        chosenGameObject = Instantiate(building.prefab);
        isChoosen = true;

        InputProvider.Instance.SwitchToBuilding();
    }

    public void ChooseBuilding(int building)
    {
        UnChooseBuilding();
        chosenBuilding = AllBuilding[building];
        chosenIndex = building;
        chosenGameObject = Instantiate(chosenBuilding.prefab);
        isChoosen = true;

        InputProvider.Instance.SwitchToBuilding();
    }

    public void UnChooseBuilding()
    {
        if (isChoosen)
        {
            chosenIndex = -1;
            isChoosen = false;
            Destroy(chosenGameObject);

            InputProvider.Instance.SwitchToGameplay();
        }
    }

    // Input handling.

    private void HandlePlace()
    {
        if (!isChoosen) return;
        StartBuilding();
    }

    private void HandleCancel()
    {
        if (!isChoosen) return;
        UnChooseBuilding();
    }

    // Building placement.

    public void StartBuilding()
    {
        if (isChoosen && chosenIndex != -1)
        {
            //if (ResourceManager.Instance.RemoveResource(chosenBuilding.Cost))
            //{
                Spawn(chosenIndex, chosenGameObject.transform.position);
            //}
        }
        UnChooseBuilding();
    }

    public void Spawn(int index, Vector3 pos)
    {
        var buffer = entityQuery.GetSingletonBuffer<BuildPrefab>(true);
        var prefab = buffer[index].Prefab;
        var e = entityManager.Instantiate(prefab);
        SpawnTransformUtility.SetLocalTransformAndLocalToWorld(
            entityManager,
            e,
            LocalTransform.FromPosition((float3)pos));
    }
}
