using System.Collections;
using System.IO;
using UnityEngine;

public class PhotoCamera : MonoBehaviour
{
    // ファインダーのRectTransform（四角いプレハブやUIオブジェクト）
    [SerializeField] private RectTransform finderRect;

    // 撮影した写真をプレビュー表示するためのUI（Imageなど。確認用）
    [SerializeField] private UnityEngine.UI.RawImage previewImage;

    void Update()
    {
        // 例えばCキーで撮影
        if (Input.GetKeyDown(KeyCode.C))
        {
            StartCoroutine(TakeDataShotRoutine());
        }
    }

    private IEnumerator TakeDataShotRoutine()
    {
        // 画面のレンダリングが完全に終了するまで待つ（超重要！）
        yield return new WaitForEndOfFrame();

        // 1. ファインダーの画面上での4隅の座標を取得
        Vector3[] corners = new Vector3[4];
        finderRect.GetWorldCorners(corners);
        // corners[0] = 左下, corners[1] = 左上, corners[2] = 右上, corners[3] = 右下

        // 2. 切り取る左下のピクセル位置と、幅・高さを計算
        int startX = Mathf.RoundToInt(corners[0].x);
        int startY = Mathf.RoundToInt(corners[0].y);
        int width = Mathf.RoundToInt(corners[2].x - corners[0].x);
        int height = Mathf.RoundToInt(corners[2].y - corners[0].y);

        // 画面外にはみ出している場合のクランプ処理（エラー防止）
        startX = Mathf.Clamp(startX, 0, Screen.width);
        startY = Mathf.Clamp(startY, 0, Screen.height);
        width = Mathf.Clamp(width, 0, Screen.width - startX);
        height = Mathf.Clamp(height, 0, Screen.height - startY);

        if (width <= 0 || height <= 0) yield break;

        // 3. 切り取ったサイズ分のテクスチャを用意
        Texture2D croppedTexture = new Texture2D(width, height, TextureFormat.RGB24, false);

        // 1. 撮影直前に枠を非表示にする
        finderRect.gameObject.SetActive(false);

        // 2. 1フレーム（描画完了）待つ
        yield return new WaitForEndOfFrame();

        // 4. 画面（バックバッファ）から指定範囲のピクセルを読み込む
        croppedTexture.ReadPixels(new Rect(startX, startY, width, height), 0, 0);
        croppedTexture.Apply();

        // 4. 撮影が終わったら枠を戻す
        finderRect.gameObject.SetActive(true);

        // --- ここで撮影成功！croppedTextureに写真が入っています ---

        // 【テスト用】UIに撮影した写真を表示してみる
        if (previewImage != null)
        {
            previewImage.texture = croppedTexture;
        }

        // 【応用】もし画像ファイル(PNG)として保存したい場合
        SaveTextureAsPNG(croppedTexture);
    }

    // （オマケ）PC内にPNGとして保存するメソッド
    private void SaveTextureAsPNG(Texture2D tex)
    {
        byte[] bytes = tex.EncodeToPNG();
        string path = Path.Combine(Application.dataPath, $"Photo_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(path, bytes);
        Debug.Log($"写真を保存しました: {path}");
    }
}