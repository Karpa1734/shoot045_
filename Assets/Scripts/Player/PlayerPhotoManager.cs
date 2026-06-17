using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks; // 対策C：非同期タスク用
using UnityEngine;
using KanKikuchi.AudioManager;

public class PlayerPhotoManager : MonoBehaviour
{
    private enum CameraState { Idle, Focusing, Shutter, Stay, Returning }
    [SerializeField] private CameraState currentState = CameraState.Idle;

    [Header("Finder Prefabs")]
    [SerializeField] private GameObject largeFinderPrefab; // 動く外枠
    [SerializeField] private GameObject smallFinderPrefab; // 常に表示されている内枠（ターゲット）
    [SerializeField] private float offsetY = 2.0f;          // 自機からのY軸オフセット（ベースの高さ）

    [Header("2つ目のフォーカス収縮エフェクト設定")]
    [SerializeField] private GameObject subFocusPrefab;     // フォーカス中に一緒に小さくなる2つ目のプレハブ
    [SerializeField] private float subStartScale = 3.0f;    // 2つ目のプレハブの「初期最大サイズ」（Unity単位）
    [SerializeField] private float subEndScale = 0.5f;      // 2つ目のプレハブの「最終最小サイズ」（Unity単位）

    // ★ VariableCommonData.txt の設定値に完全準拠
    private const float MAX_CAMERA_SCALE = 4.0f;
    private const float MIN_CAMERA_SCALE = 0.7f;
    private const float FOCUS_REDUCTION_RATE = 0.07f;
    private const float PIXEL_TO_WORLD_FACTOR = 64f;

    [Header("弾幕風準拠 of 配置設定")]
    [SerializeField] private float cameraRD = 48f;
    [SerializeField] private float maxSlowRD = 40f;
    [SerializeField] private float slowTransitionSpeed = 5f;
    private float currentSlowRD = 0f;

    [Header("フォーカス移動（望遠・エイム）設定")]
    [SerializeField] private float focusMoveSpeed = 4.0f;

    [Header("追従・復帰の滑らかさ設定")]
    [SerializeField] private float finderFollowSpeed = 8.0f;
    [SerializeField] private float returnToDefaultSpeed = 5.0f;
    [SerializeField] private float lockOnRotateSpeed = 45.0f;

    [Header("写真の保存・書き出し設定")]
    [SerializeField] private bool saveToLocalFolder = true;
    [SerializeField] private string folderName = "SavedPhotos";

    // ★UI側のエラー（CS0123）を根絶するため、通知のパイプを「Texture2D, float」の2引数に完全適合！
    public static System.Action<Texture2D, float> OnPhotoCaptured;

    private float currentFinderAngle = 90f;
    private float currentRadius = 2.48f;

    private const float PPU = 100f;

    private GameObject activeLargeFinder;
    private GameObject activeSmallFinder;
    private GameObject activeSubFocus;
    private SpriteRenderer largeFinderSr;
    private SpriteRenderer subFocusSr;
    private float currentCameraScale;

    private Vector3 currentFinderCenter;
    private Quaternion currentFinderRotation = Quaternion.identity;

    // 対策D：NonAlloc用の固定解放バッファ
    private Collider2D[] overlapBuffer = new Collider2D[1024];

    // 対策E：敵探索キャッシュタイマー
    private GameObject cachedClosestEnemy;
    private float enemyCacheTimer = 0f;
    private const float ENEMY_CACHE_INTERVAL = 0.1f;

    [Header("References")]
    private PlayerHitHandler hitHandler;
    private Camera mainCamera;

    void Start()
    {
        hitHandler = GetComponentInChildren<PlayerHitHandler>();
        if (hitHandler == null) hitHandler = GetComponent<PlayerHitHandler>();
        mainCamera = Camera.main;

        if (smallFinderPrefab != null)
        {
            activeSmallFinder = Instantiate(smallFinderPrefab, transform.position, Quaternion.identity);
            float smallWorldSize = 0.8f;
            activeSmallFinder.transform.localScale = new Vector3(smallWorldSize, smallWorldSize, 1f);
        }

        float defaultDistance = (cameraRD / PPU) + offsetY;
        currentFinderCenter = transform.position + new Vector3(0f, defaultDistance, 0f);
        currentRadius = defaultDistance;
    }

    void Update()
    {
        if (Time.timeScale <= 0) return;

        UpdateFinderPosition();

        if (currentState == CameraState.Focusing && (hitHandler == null || hitHandler.currentState != PlayerHitHandler.PlayerState.DeathBomb))
        {
            CancelFocus();
            return;
        }

        if (currentState == CameraState.Idle && hitHandler != null && hitHandler.currentState == PlayerHitHandler.PlayerState.Normal)
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                StartFocus();
            }
        }

        if (currentState == CameraState.Focusing && Input.GetKeyUp(KeyCode.Z))
        {
            StartCoroutine(CapturePhotoRoutine());
        }
    }

    void FixedUpdate()
    {
        if (currentState == CameraState.Focusing && activeLargeFinder != null)
        {
            if (Input.GetKey(KeyCode.Z))
            {
                currentCameraScale -= FOCUS_REDUCTION_RATE;

                if (currentCameraScale <= MIN_CAMERA_SCALE)
                {
                    Destroy(activeLargeFinder);
                    if (activeSubFocus != null) Destroy(activeSubFocus);
                    if (hitHandler != null) hitHandler.currentState = PlayerHitHandler.PlayerState.Normal;
                    StartCoroutine(StayAndReturnRoutine());
                    return;
                }

                float currentWorldSize = (currentCameraScale * PIXEL_TO_WORLD_FACTOR) / PPU;
                activeLargeFinder.transform.localScale = new Vector3(currentWorldSize, currentWorldSize, 1f);

                if (activeSubFocus != null)
                {
                    float progress = Mathf.InverseLerp(MAX_CAMERA_SCALE, MIN_CAMERA_SCALE, currentCameraScale);
                    float subCurrentScale = Mathf.Lerp(subStartScale, subEndScale, progress);
                    activeSubFocus.transform.localScale = new Vector3(subCurrentScale, subCurrentScale, 1f);
                }

                HandleFocusMovement();
            }
        }
    }

    private void UpdateFinderPosition()
    {
        bool isSlow = Input.GetKey(KeyCode.LeftShift);
        float targetSlowRD = isSlow ? maxSlowRD : 0f;
        currentSlowRD = Mathf.MoveTowards(currentSlowRD, targetSlowRD, slowTransitionSpeed * maxSlowRD * Time.deltaTime);
        float DashboardProgress = currentSlowRD / maxSlowRD;

        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");
        Vector3 moveDir = new Vector3(moveX, moveY, 0f);

        float defaultRadius = (cameraRD / PPU) + offsetY;
        float lockOnRadius = (cameraRD + maxSlowRD) / PPU + offsetY;

        float targetRadius = defaultRadius;
        float targetAngle = 90f;
        float rotationSpeedFactor = 25f;

        if (currentState == CameraState.Idle || currentState == CameraState.Returning)
        {
            enemyCacheTimer += Time.deltaTime;
            if (enemyCacheTimer >= ENEMY_CACHE_INTERVAL)
            {
                enemyCacheTimer = 0f;
                cachedClosestEnemy = isSlow ? FindClosestEnemy() : null;
            }

            if (cachedClosestEnemy != null && cachedClosestEnemy.activeInHierarchy)
            {
                Vector3 dirToEnemy = cachedClosestEnemy.transform.position - transform.position;
                if (dirToEnemy.sqrMagnitude > 0.01f)
                {
                    targetAngle = Mathf.Atan2(dirToEnemy.y, dirToEnemy.x) * Mathf.Rad2Deg;
                }
                targetRadius = Mathf.Lerp(defaultRadius, lockOnRadius, DashboardProgress);
                rotationSpeedFactor = lockOnRotateSpeed;
            }
            else
            {
                targetRadius = defaultRadius;

                if (moveDir.sqrMagnitude > 0.01f && currentState == CameraState.Idle)
                {
                    targetAngle = Mathf.Atan2(moveY, moveX) * Mathf.Rad2Deg;
                }
                else
                {
                    targetAngle = 90f;
                }

                float baseSpeed = (currentState == CameraState.Returning) ? returnToDefaultSpeed : finderFollowSpeed;
                rotationSpeedFactor = baseSpeed * 25f;
            }

            if (Mathf.Abs(targetAngle - 90f) < 0.01f && Mathf.Abs(currentFinderAngle - 90f) < 0.1f)
            {
                currentFinderAngle = 90f;
            }
            else
            {
                currentFinderAngle = Mathf.MoveTowardsAngle(currentFinderAngle, targetAngle, rotationSpeedFactor * Time.deltaTime);
            }

            float radiusSpeed = (currentState == CameraState.Returning) ? returnToDefaultSpeed : finderFollowSpeed;
            currentRadius = Mathf.Lerp(currentRadius, targetRadius, radiusSpeed * Time.deltaTime);

            float rad = currentFinderAngle * Mathf.Deg2Rad;
            Vector3 radialOffset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * currentRadius;

            currentFinderCenter = transform.position + radialOffset;
            currentFinderRotation = Quaternion.Euler(0f, 0f, currentFinderAngle - 90f);

            if (activeSmallFinder != null)
            {
                activeSmallFinder.transform.position = currentFinderCenter;
                activeSmallFinder.transform.rotation = currentFinderRotation;
            }

            if (currentState == CameraState.Returning && Mathf.Abs(currentFinderAngle - targetAngle) < 0.1f && Mathf.Abs(currentRadius - targetRadius) < 0.05f)
            {
                currentState = CameraState.Idle;
            }
        }
        else if (currentState == CameraState.Focusing)
        {
            if (activeSmallFinder != null) activeSmallFinder.transform.position = currentFinderCenter;
            if (activeLargeFinder != null) activeLargeFinder.transform.position = currentFinderCenter;
            if (activeSubFocus != null) activeSubFocus.transform.position = currentFinderCenter;

            Vector3 offsetFromPlayer = currentFinderCenter - transform.position;
            currentRadius = offsetFromPlayer.magnitude;
            if (offsetFromPlayer.sqrMagnitude > 0.01f)
            {
                currentFinderAngle = Mathf.Atan2(offsetFromPlayer.y, offsetFromPlayer.x) * Mathf.Rad2Deg;
            }
        }
        else if (currentState == CameraState.Stay)
        {
            if (activeSmallFinder != null)
            {
                activeSmallFinder.transform.position = currentFinderCenter;
                activeSmallFinder.transform.rotation = currentFinderRotation;
            }

            Vector3 offsetFromPlayer = currentFinderCenter - transform.position;
            currentRadius = offsetFromPlayer.magnitude;
            if (offsetFromPlayer.sqrMagnitude > 0.01f)
            {
                currentFinderAngle = Mathf.Atan2(offsetFromPlayer.y, offsetFromPlayer.x) * Mathf.Rad2Deg;
            }
        }
    }

    private IEnumerator StayAndReturnRoutine()
    {
        currentState = CameraState.Stay;
        yield return new WaitForSeconds(0.5f);
        currentState = CameraState.Returning;
    }

    private GameObject FindClosestEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        GameObject closest = null;
        float minDistance = Mathf.Infinity;
        Vector3 currentPos = transform.position;

        foreach (GameObject enemy in enemies)
        {
            if (!enemy.activeInHierarchy) continue;

            float dist = Vector3.Distance(enemy.transform.position, currentPos);
            if (dist < minDistance)
            {
                closest = enemy;
                minDistance = dist;
            }
        }
        return closest;
    }

    private void HandleFocusMovement()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        if (moveX != 0f || moveY != 0f)
        {
            Vector3 moveDirection = new Vector3(moveX, moveY, 0f).normalized;
            currentFinderCenter += moveDirection * focusMoveSpeed * Time.fixedDeltaTime;

            if (mainCamera != null)
            {
                Vector3 viewPos = mainCamera.WorldToViewportPoint(currentFinderCenter);
                viewPos.x = Mathf.Clamp(viewPos.x, 0.05f, 0.95f);
                viewPos.y = Mathf.Clamp(viewPos.y, 0.05f, 0.95f);
                currentFinderCenter = mainCamera.ViewportToWorldPoint(viewPos);
                currentFinderCenter.z = 0f;
            }
        }
    }

    private void StartFocus()
    {
        SEManager.Instance.Play(SEPath.SE_FOCUS, 0.5f);
        currentState = CameraState.Focusing;
        hitHandler.currentState = PlayerHitHandler.PlayerState.DeathBomb;

        activeLargeFinder = Instantiate(largeFinderPrefab, currentFinderCenter, Quaternion.identity);
        largeFinderSr = activeLargeFinder.GetComponent<SpriteRenderer>();
        if (largeFinderSr == null) largeFinderSr = activeLargeFinder.GetComponentInChildren<SpriteRenderer>();

        if (subFocusPrefab != null)
        {
            activeSubFocus = Instantiate(subFocusPrefab, currentFinderCenter, Quaternion.identity);
            subFocusSr = activeSubFocus.GetComponent<SpriteRenderer>();
            if (subFocusSr == null) subFocusSr = activeSubFocus.GetComponentInChildren<SpriteRenderer>();
        }

        if (activeSmallFinder != null)
        {
            activeLargeFinder.transform.rotation = activeSmallFinder.transform.rotation;
            if (activeSubFocus != null) activeSubFocus.transform.rotation = activeSmallFinder.transform.rotation;
        }

        currentCameraScale = MAX_CAMERA_SCALE;
        float initialWorldSize = (currentCameraScale * PIXEL_TO_WORLD_FACTOR) / PPU;
        activeLargeFinder.transform.localScale = new Vector3(initialWorldSize, initialWorldSize, 1f);

        if (activeSubFocus != null)
        {
            activeSubFocus.transform.localScale = new Vector3(subStartScale, subStartScale, 1f);
        }
    }

    private IEnumerator CapturePhotoRoutine()
    {
        currentState = CameraState.Shutter;

        yield return new WaitForEndOfFrame();

        if (activeLargeFinder != null && largeFinderSr != null && largeFinderSr.sprite != null)
        {
            float shotAngle = activeLargeFinder.transform.eulerAngles.z;
            Vector2 finderSpriteSize = largeFinderSr.sprite.bounds.size;
            Vector2 finderWorldSize = new Vector2(finderSpriteSize.x * activeLargeFinder.transform.localScale.x, finderSpriteSize.y * activeLargeFinder.transform.localScale.y);

            // 撮影瞬間の現在の縮小比率
            float currentSqueezeRatio = currentCameraScale / MAX_CAMERA_SCALE;

            largeFinderSr.enabled = false;
            if (subFocusSr != null) subFocusSr.enabled = false;
            SpriteRenderer smallFinderSr = activeSmallFinder != null ? activeSmallFinder.GetComponent<SpriteRenderer>() : null;
            if (smallFinderSr != null) smallFinderSr.enabled = false;

            int originalMask = mainCamera.cullingMask;
            int gameUiLayerBit = 1 << LayerMask.NameToLayer("GameUI");
            int normalUiLayerBit = 1 << LayerMask.NameToLayer("UI");
            mainCamera.cullingMask &= ~(gameUiLayerBit | normalUiLayerBit);

            yield return new WaitForEndOfFrame();

            Vector3 centerPos = activeLargeFinder.transform.position;

            // 💡【等倍固定の切り出し】：ボスの倍率を固定するため、最大時のワールドサイズで広く安全にキャプチャ
            float maxWorldWidth = finderSpriteSize.x * (MAX_CAMERA_SCALE * PIXEL_TO_WORLD_FACTOR / PPU);
            float maxWorldHeight = finderSpriteSize.y * (MAX_CAMERA_SCALE * PIXEL_TO_WORLD_FACTOR / PPU);
            float diagonalWorldSize = Mathf.Sqrt((maxWorldWidth * maxWorldWidth) + (maxWorldHeight * maxWorldHeight));

            Vector3 safeLeftBottomWorld = centerPos - new Vector3(diagonalWorldSize / 2f, diagonalWorldSize / 2f, 0f);
            Vector3 safeRightTopWorld = centerPos + new Vector3(diagonalWorldSize / 2f, diagonalWorldSize / 2f, 0f);

            Vector2 screenLeftBottom = mainCamera.WorldToScreenPoint(safeLeftBottomWorld);
            Vector2 screenRightTop = mainCamera.WorldToScreenPoint(safeRightTopWorld);

            // はみ出し完全ガードクランプ
            int startX = Mathf.Clamp(Mathf.RoundToInt(screenLeftBottom.x), 0, Screen.width);
            int startY = Mathf.Clamp(Mathf.RoundToInt(screenLeftBottom.y), 0, Screen.height);
            int endX = Mathf.Clamp(Mathf.RoundToInt(screenRightTop.x), 0, Screen.width);
            int endY = Mathf.Clamp(Mathf.RoundToInt(screenRightTop.y), 0, Screen.height);

            int width = endX - startX;
            int height = endY - startY;

            if (width > 0 && height > 0)
            {
                // 大きな器（1:1正方形）にマルチカメラ背景付きの画面データを直撃 ReadPixels 撮影
                Texture2D photoTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                photoTex.ReadPixels(new Rect(startX, startY, width, height), 0, 0);

                // 💡【ご提案アプローチ】：
                // 撮影した瞬間のファインダーサイズ（diagonalWorldSize * currentSqueezeRatio）をマスク関数にストレートに代入！
                // これにより、中身のボスのスケールはそのまま等倍で、ファインダーの外側だけが完璧に透明化されます。
                float currentMaskWorldSize = diagonalWorldSize * currentSqueezeRatio;
                ApplyInvertedRectangleMaskOptimized(photoTex, finderWorldSize, shotAngle, currentMaskWorldSize, currentMaskWorldSize);
                photoTex.Apply();

                SEManager.Instance.Play(SEPath.SE_SHUTTER, 0.5f);

                // 💡【透明化バグの元凶全撤廃】：
                // 変なBlitやDrawTextureでの2重トリミングを完全に削除！
                // 綺麗にマスクされた1枚の完成写真を、縮小率（currentSqueezeRatio）と一緒にUIへ直に手渡します。
                OnPhotoCaptured?.Invoke(photoTex, currentSqueezeRatio);

                // 対策C：非同期保存
                if (saveToLocalFolder)
                {
                    SaveTextureToLocalPNGAsynchronous(photoTex);
                }

                // 対策D：NonAlloc弾消去
                Bounds bounds = largeFinderSr.bounds;
                Vector2 boxSize = new Vector2(bounds.size.x, bounds.size.y);
                int hitCount = Physics2D.OverlapBoxNonAlloc(bounds.center, boxSize, shotAngle, overlapBuffer);

                for (int i = 0; i < hitCount; i++)
                {
                    Collider2D hit = overlapBuffer[i];
                    if (hit != null && hit.CompareTag("EnemyBullet"))
                    {
                        EnemyBullet bullet = hit.GetComponent<EnemyBullet>();
                        if (bullet != null) bullet.Deactivate(true);
                    }
                }
            }

            mainCamera.cullingMask = originalMask;
            if (smallFinderSr != null) smallFinderSr.enabled = true;
        }

        if (activeLargeFinder != null) Destroy(activeLargeFinder);
        if (activeSubFocus != null) Destroy(activeSubFocus);

        if (hitHandler != null) hitHandler.currentState = PlayerHitHandler.PlayerState.Normal;

        StartCoroutine(StayAndReturnRoutine());
    }

    private void ApplyInvertedRectangleMaskOptimized(Texture2D tex, Vector2 finderWorldSize, float angleDeg, float maxW, float maxH)
    {
        Color[] pixels = tex.GetPixels();
        float angleRad = -angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(angleRad);
        float sin = Mathf.Sin(angleRad);

        float halfW = finderWorldSize.x / 2f;
        float halfH = finderWorldSize.y / 2f;

        int texW = tex.width;
        int texH = tex.height;

        for (int y = 0; y < texH; y++)
        {
            float normY = ((float)y / texH) - 0.5f;
            float worldY = normY * maxH;

            for (int x = 0; x < texW; x++)
            {
                int index = y * texW + x;

                float normX = ((float)x / texW) - 0.5f;
                float worldX = normX * maxW;

                float rotatedX = worldX * cos - worldY * sin;
                float rotatedY = worldX * sin + worldY * cos;

                if (Mathf.Abs(rotatedX) > halfW || Mathf.Abs(rotatedY) > halfH)
                {
                    pixels[index] = new Color(0f, 0f, 0f, 0f);
                }
            }
        }
        tex.SetPixels(pixels);
    }

    private void SaveTextureToLocalPNGAsynchronous(Texture2D targetTex)
    {
        string directoryPath = Path.Combine(Application.dataPath, folderName);
        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"Photo_{timestamp}.png";
        string fullPath = Path.Combine(directoryPath, fileName);

        Color[] rawPixels = targetTex.GetPixels();
        int width = targetTex.width;
        int height = targetTex.height;

        Task.Run(() =>
        {
            try
            {
                Texture2D threadSafeTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                threadSafeTex.SetPixels(rawPixels);
                byte[] bytes = threadSafeTex.EncodeToPNG();
                File.WriteAllBytes(fullPath, bytes);
                Debug.Log($"[PhotoSaved (Async)] 写真の非同期保存が完了しました: {fullPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"写真の非同期ローカル保存に失敗しました: {e.Message}");
            }
        });
    }

    private void CancelFocus()
    {
        if (activeLargeFinder != null) Destroy(activeLargeFinder);
        if (activeSubFocus != null) Destroy(activeSubFocus);
        currentState = CameraState.Idle;
    }

    private void OnDestroy()
    {
        if (activeSmallFinder != null) Destroy(activeSmallFinder);
        if (activeSubFocus != null) Destroy(activeSubFocus);
    }
}