using System;
using UnityEngine;
using UnityEngine.Rendering.Denoising;

namespace RayStorm
{
    public static class RSDenoiseUtility
    {
        public static void Denoise(RenderTexture Source, RenderTexture Destination)
        {
            if (Source == Destination || Source.width != Destination.width || Source.height != Destination.height)
                throw new ArgumentException("Denoising requires a separate destination with the same dimensions as the source.");

            var ImageDenoiser = new Denoiser();
            bool Initialized = false;
            Texture2D SourceTexture = null;
            Texture2D ResultTexture = null;
            try
            {
                if (ImageDenoiser.Init(DenoiserType.OpenImageDenoise, Source.width, Source.height) != Denoiser.State.Success)
                    throw new InvalidOperationException("Could not initialize Open Image Denoise.");
                Initialized = true;

                SourceTexture = RSDataUtility.RenderTextureToTexture2D(Source, TextureFormat.RGBAFloat);
                ResultTexture = new Texture2D(Source.width, Source.height, TextureFormat.RGBAFloat, false, true);
                var SourcePixels = SourceTexture.GetRawTextureData<Vector4>();
                var ResultPixels = ResultTexture.GetRawTextureData<Vector4>();
                if (ImageDenoiser.DenoiseRequest("color", SourcePixels) != Denoiser.State.Success)
                    throw new InvalidOperationException("Open Image Denoise rejected the HDR color buffer.");
                if (ImageDenoiser.GetResults(ResultPixels) != Denoiser.State.Success)
                    throw new InvalidOperationException("Open Image Denoise failed to produce a result.");

                // Alpha stores the original sample count, not image opacity.
                for (int PixelIndex = 0; PixelIndex < ResultPixels.Length; ++PixelIndex)
                {
                    Vector4 Pixel = ResultPixels[PixelIndex];
                    Pixel.w = SourcePixels[PixelIndex].w;
                    ResultPixels[PixelIndex] = Pixel;
                }
                ResultTexture.Apply(false, false);
                Graphics.CopyTexture(ResultTexture, Destination);
            }
            finally
            {
                if (Initialized)
                    ImageDenoiser.DisposeDenoiser();
                DestroyTexture(SourceTexture);
                DestroyTexture(ResultTexture);
            }
        }

        private static void DestroyTexture(Texture2D Texture)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(Texture);
            else
                UnityEngine.Object.DestroyImmediate(Texture);
        }
    }
}
