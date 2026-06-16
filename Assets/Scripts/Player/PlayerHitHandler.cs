using KanKikuchi.AudioManager;
using System.Collections;
using TMPro;
using UnityEngine;

public class PlayerHitHandler : MonoBehaviour
{
    public enum PlayerState { Normal, DeathBomb, Hit, Down, Rebirth }
    public PlayerState currentState = PlayerState.Normal;

    [Header("Settings")]
    public float deathBombWindow = 0.15f;
    public float invincibilityTime = 3.0f;
    public float downTime = 0.8f;

    [Header("References")]
    public GameObject explosionEffectPrefab;
    public PlayerAnimation playerAnim;
    public PlayerMove playerMove;

    [Header("Bullet Clear")]
    public GameObject bulletClearPrefab;

    private SpriteRenderer characterRenderer;
    private ItemEffectHandler itemHandler;

    void Awake()
    {
        if (playerMove == null) playerMove = GetComponentInParent<PlayerMove>();
        if (playerAnim == null) playerAnim = GetComponentInParent<PlayerAnimation>();
        itemHandler = GetComponent<ItemEffectHandler>();

        characterRenderer = GetComponentInParent<SpriteRenderer>();
        if (characterRenderer == null)
        {
            characterRenderer = transform.parent.GetComponentInChildren<SpriteRenderer>();
        }
    }

    void Update()
    {
        if (playerMove != null && playerAnim != null)
        {
            playerAnim.isInvincible = playerMove.IsInvincible;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // アイテムの回収処理（無敵や状態に関わらず常に判定する）
        if (collision.CompareTag("Item"))
        {
            itemHandler.HandleItemCollision(collision);
            return;
        }

        // ★最重要：無敵中、またはすでに被弾（デスボム受付など）状態であれば、以降の被弾処理を一切無視する
        if (playerMove.IsInvincible || currentState != PlayerState.Normal) return;

        // 被弾判定（敵弾、敵本体、レーザー）
        if (collision.CompareTag("EnemyBullet") || collision.CompareTag("Enemy") || collision.CompareTag("Laser"))
        {
            EnemyStatus boss = Object.FindFirstObjectByType<EnemyStatus>();
            if (boss != null) boss.FailSpell();

            // 被弾した瞬間に時間を通常速度に戻す（低速処理などのリセット用）
            Time.timeScale = 1.0f;
            Time.fixedDeltaTime = 0.02f;

            // 状態をデスボム受付中に変更し、コルーチンを開始
            currentState = PlayerState.DeathBomb;
            StartCoroutine(CheckDeathBombRoutine());
        }
    }

    IEnumerator CheckDeathBombRoutine()
    {
        SEManager.Instance.Play(SEPath.SE_PLAYER_COLLISION, 0.3f);
        playerMove.StartDeathBombWindow(deathBombWindow);

        while (playerMove.IsInDeathBombWindow)
        {
            // ボムが発動して無敵状態になったら即座にループを抜ける
            if (playerMove.IsInvincible) break;
            yield return null;
        }

        // デスボム成功などにより、受付時間中に無敵状態（あるいはNormal）に戻っていた場合の安全弁
        if (playerMove.IsInvincible)
        {
            currentState = PlayerState.Normal;
            yield break;
        }

        StartCoroutine(ExplosionAndRebirthRoutine());
    }

    IEnumerator ExplosionAndRebirthRoutine()
    {
        if (playerMove.IsInvincible)
        {
            currentState = PlayerState.Normal;
            yield break;
        }

        Vector3 deathPos = transform.position;
        currentState = PlayerState.Hit;

        if (explosionEffectPrefab != null) Instantiate(explosionEffectPrefab, deathPos, Quaternion.identity);
        if (bulletClearPrefab != null)
        {
            GameObject clearObj = Instantiate(bulletClearPrefab);
            clearObj.SendMessage("StartClearing", deathPos, SendMessageOptions.DontRequireReceiver);
        }

        playerMove.enabled = false;
        transform.parent.position = new Vector3(-2.0f, -100f, 0); // 画面外へ
        if (characterRenderer != null) characterRenderer.enabled = false; // 非表示

        if (PlayerStatusManager.Instance.SubtractLifeAndCheckRebirth())
        {
            yield return new WaitForSeconds(downTime);

            currentState = PlayerState.Rebirth;
            transform.parent.position = new Vector3(-2.0f, -6.0f, 0);
            if (characterRenderer != null) characterRenderer.enabled = true;

            float elapsed = 0;
            Vector3 startPos = transform.parent.position;
            Vector3 targetPos = new Vector3(-2.0f, -3.5f, 0);
            while (elapsed < 0.6f)
            {
                transform.parent.position = Vector3.Lerp(startPos, targetPos, elapsed / 0.6f);
                elapsed += Time.deltaTime;
                yield return null;
            }

            playerMove.enabled = true;
            currentState = PlayerState.Normal;
            playerMove.SetInvincible(invincibilityTime); // ここで無敵が付与される
        }
        else
        {
            yield return new WaitForSeconds(0.5f);
            PlayerStatusManager.Instance.TriggerGameOver();
            yield break;
        }
    }

    public void StartRebirthFromContinue()
    {
        StartCoroutine(RebirthRoutine());
    }

    private IEnumerator RebirthRoutine()
    {
        currentState = PlayerState.Rebirth;

        transform.parent.position = new Vector3(-2.0f, -6.0f, 0);
        if (characterRenderer != null) characterRenderer.enabled = true;

        float elapsed = 0;
        Vector3 startPos = transform.parent.position;
        Vector3 targetPos = new Vector3(-2.0f, -3.5f, 0);

        while (elapsed < 0.6f)
        {
            transform.parent.position = Vector3.Lerp(startPos, targetPos, elapsed / 0.6f);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        playerMove.enabled = true;
        currentState = PlayerState.Normal;
        playerMove.SetInvincible(invincibilityTime);
    }
}