using KanKikuchi.AudioManager;
using UnityEngine;

public class PlayerShotManager : MonoBehaviour
{
    // 既存の変数はインスペクターエラー防止のために残します
    [Header("Prefabs")] public GameObject mainShotPrefab; public GameObject needlePrefab; public GameObject homingPrefab;
    [Header("Interval Settings")] public float mainShotInterval = 0.05f; public float needleInterval = 0.08f; public float homingInterval = 0.12f;
    private float mainTimer; private float subTimer; private OptionManager optionManager; private float[] homingInitialAngles = { -20f, 20f, 10f, -10f };
    private PlayerHitHandler hitHandler;

    void Start()
    {
        optionManager = GetComponent<OptionManager>();
        hitHandler = GetComponentInChildren<PlayerHitHandler>();
        if (hitHandler == null) Debug.LogError("PlayerHitHandler が子オブジェクトに見つかりません！");
    }

    void Update()
    {
        // ★撮影モード用にショットを完全封印するため、Update処理を無効化
        return;

        /* 以前のZキーショット処理はすべてスキップされます */
    }

    void FireMainShot() { }
    void FireSubShot(bool isSlow) { }
    void Spawn(GameObject prefab, Vector3 pos) { }
}