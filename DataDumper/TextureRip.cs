using UnityEngine;

namespace DysonSphereProgram.Modding.DataDumper;

public readonly ref struct TemporaryActiveRenderTextureContext
{
  private readonly RenderTexture activeTexRestore;

  public TemporaryActiveRenderTextureContext(RenderTexture tex)
  {
    activeTexRestore = RenderTexture.active;
    RenderTexture.active = tex;
  }

  public void Dispose()
  {
    RenderTexture.active = activeTexRestore;
  }
}

public static class TextureRip
{
  private static byte[] GetPngBytes(Texture texture, int width, int height)
  {
    var output = new Texture2D(width, height);
      
    var tmpRenderTexture =
      RenderTexture.GetTemporary(
        output.width,
        output.height,
        0,
        RenderTextureFormat.Default,
        RenderTextureReadWrite.sRGB
      );
      
    Graphics.Blit(texture, tmpRenderTexture);
    using (var _ = new TemporaryActiveRenderTextureContext(tmpRenderTexture))
    {
      output.ReadPixels(new Rect(0, 0, tmpRenderTexture.width, tmpRenderTexture.height), 0, 0);
      output.Apply();
    }
    
    RenderTexture.ReleaseTemporary(tmpRenderTexture);

    return output.EncodeToPNG();
  }
  
  public static byte[] GetPngBytes(Texture texture)
  {
    return GetPngBytes(texture, texture.width, texture.height);
  }
  
  public static byte[] GetPngBytes(Sprite sprite)
  {
    var r = sprite.rect;
    return GetPngBytes(sprite.texture, (int)r.width, (int)r.height);
  }
}
