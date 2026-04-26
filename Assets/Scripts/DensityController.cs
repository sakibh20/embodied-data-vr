using UnityEngine;
using System.Collections.Generic;

public class DensityController : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject spawnPrefab;

    [Header("Area Settings")]
    [SerializeField] private Vector2 areaSize = new Vector2(5f, 5f);
    [SerializeField] private Transform centerPoint;

    [Header("Density Settings")]
    [SerializeField] private int minCount = 5;
    [SerializeField] private int maxCount = 100;

    [Header("Size Settings")]
    [SerializeField] private Vector2 sizeRange = new Vector2(0.1f, 0.3f);

    private readonly List<GameObject> _pool = new List<GameObject>();

    public void UpdateDensity(float normalizedValue)
    {
        int targetCount = Mathf.RoundToInt(Mathf.Lerp(minCount, maxCount, normalizedValue));

        AdjustPool(targetCount);
    }

    private void AdjustPool(int targetCount)
    {
        // Add objects
        while (_pool.Count < targetCount)
        {
            GameObject obj = CreateObject();
            _pool.Add(obj);
        }

        // Remove objects
        while (_pool.Count > targetCount)
        {
            GameObject obj = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            Destroy(obj);
        }
    }

    private GameObject CreateObject()
    {
        Vector3 pos = GetRandomPosition();

        GameObject obj = Instantiate(spawnPrefab, pos, Quaternion.identity, transform);

        float scale = Random.Range(sizeRange.x, sizeRange.y);
        obj.transform.localScale = Vector3.one * scale;

        return obj;
    }

    private Vector3 GetRandomPosition()
    {
        Vector3 center = centerPoint != null ? centerPoint.position : transform.position;

        float x = Random.Range(-areaSize.x / 2f, areaSize.x / 2f);
        float z = Random.Range(-areaSize.y / 2f, areaSize.y / 2f);

        return new Vector3(center.x + x, center.y, center.z + z);
    }
}