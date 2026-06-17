using UnityEngine;
using UnityEngine.UI;

public class PhotoUiViewer : MonoBehaviour
{
    [SerializeField] private RawImage targetUiImage; // 写真を表示したいUIのRawImage

    private AspectRatioFitter aspectRatioFitter;

    // ★追加：インスペクターで設定する、現像写真の「最大（ベース）のUIサイズ」
    // 例えば、ファインダー最大時に左下に横240px・縦180pxの大きさで表示したい場合は、ここに数値を入力します。
    [Header("ベースとなる最大のUIサイズ設定（ファインダー最大時）")]
    [SerializeField] private Vector2 maxUiSize = new Vector2(240f, 180f);

    void Awake()
    {
        if (targetUiImage != null)
        {
            // 同じオブジェクトについているAspect Ratio Fitterを取得
            aspectRatioFitter = targetUiImage.GetComponent<AspectRatioFitter>();
        }
    }

    void OnEnable()
    {
        // ★修正ポイント：PlayerPhotoManager側の通知引数（Texture2D, float）に完全に購読を適合させます！
        PlayerPhotoManager.OnPhotoCaptured += DisplayCapturedPhoto;
    }

    void OnDisable()
    {
        // 購読解除
        PlayerPhotoManager.OnPhotoCaptured -= DisplayCapturedPhoto;
    }

    // ★修正ポイント：第2引数に「撮影当時のファインダー縮小割合(float)」を受け取る
    private void DisplayCapturedPhoto(Texture2D photoTexture, float scaleRatio)
    {
        if (targetUiImage == null || photoTexture == null) return;

        // 1. UIのテクスチャを切り替え
        targetUiImage.texture = photoTexture;

        // 2. 歪み解決核心：テクスチャ本来の純粋な縦横解像度から正確なアスペクト比を計算してFitterに適用
        float width = photoTexture.width;
        float height = photoTexture.height;
        float photoAspect = width / height;

        if (aspectRatioFitter != null)
        {
            aspectRatioFitter.aspectRatio = photoAspect;
        }

        // 💡【真のサイズ可変現像】：
        // 撮影した瞬間のファインダーの小ささ（scaleRatio）をベースUIサイズに乗算して、枠自体を物理的にきゅっと小さく変形！
        // これにより、ボスの大きさ（倍率）は等倍のまま変わらず、写真カード自体のサイズだけがファインダー通りに小さくなります！
        targetUiImage.rectTransform.sizeDelta = new Vector2(maxUiSize.x * scaleRatio, maxUiSize.y * scaleRatio);

        // 写真自体は回転させず真っ直ぐ（identity）に固定
        targetUiImage.rectTransform.localRotation = Quaternion.identity;
    }
}