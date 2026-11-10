using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RayStorm
{
	public static class RSDataUtility
	{
		public delegate T DelegateDataConvert<in S, out T>(S Data);

		////////////////////////////////////////////////////////////////////////////////////////////////////
		/// Common Helper Functions |
		////////////////////////////////////////////////////////////////////////////////////////////////////

		public static bool IsNullOrEmpty<T>(T[] SourceArray)
		{
			return SourceArray == null || SourceArray.Length == 0;
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////
		/// Data Convert Functions |
		////////////////////////////////////////////////////////////////////////////////////////////////////

		public static Vector3 ColorToVector(Color Color)
		{
			return new Vector3(Color.r, Color.g, Color.b);
		}

		public static Color VectorToColor(Vector3 Vector)
		{
			return new Color(Mathf.Clamp01(Vector.x), Mathf.Clamp01(Vector.y), Mathf.Clamp01(Vector.z));
		}

		public static T[] ConvertArray<S, T>(S[] SourceArray, DelegateDataConvert<S, T> ConvertFunction)
		{
			if (SourceArray == null)
				return null;

			T[] OutputArray = new T[SourceArray.Length];
			for (int ElementID = 0; ElementID < SourceArray.Length; ++ElementID)
				OutputArray[ElementID] = ConvertFunction(SourceArray[ElementID]);

			return OutputArray;
		}

		public static Texture2D RenderTextureToTexture2D(RenderTexture SourceRenderTexture, TextureFormat Format, bool Mipmap = false)
		{
			if (SourceRenderTexture == null)
				throw new System.ArgumentNullException(nameof(SourceRenderTexture));

			bool IsHDR = Format == TextureFormat.RGBAHalf || Format == TextureFormat.RGBAFloat;
			Texture2D ReadbackTexture = new Texture2D(SourceRenderTexture.width, SourceRenderTexture.height,
				IsHDR ? Format : TextureFormat.RGBAFloat, Mipmap, true);
			Texture2D OutputTexture = null;
			RenderTexture OriginActiveRenderTexture = RenderTexture.active;
			try
			{
				RenderTexture.active = SourceRenderTexture;
				ReadbackTexture.ReadPixels(new Rect(0, 0, SourceRenderTexture.width, SourceRenderTexture.height), 0, 0);
				ReadbackTexture.Apply(Mipmap, false);
				if (IsHDR)
				{
					OutputTexture = ReadbackTexture;
					ReadbackTexture = null;
					return OutputTexture;
				}

				// Match the display pass: exposure 1, Reinhard, then encode LDR files as sRGB.
				Color[] Pixels = ReadbackTexture.GetPixels();
				for (int PixelIndex = 0; PixelIndex < Pixels.Length; ++PixelIndex)
				{
					Color Pixel = Pixels[PixelIndex];
					Pixel.r = Mathf.Max(0.0f, Pixel.r);
					Pixel.g = Mathf.Max(0.0f, Pixel.g);
					Pixel.b = Mathf.Max(0.0f, Pixel.b);
					Pixels[PixelIndex] = new Color(
						Mathf.LinearToGammaSpace(Pixel.r / (1.0f + Pixel.r)),
						Mathf.LinearToGammaSpace(Pixel.g / (1.0f + Pixel.g)),
						Mathf.LinearToGammaSpace(Pixel.b / (1.0f + Pixel.b)), 1.0f);
				}
				OutputTexture = new Texture2D(SourceRenderTexture.width, SourceRenderTexture.height, Format, Mipmap, false);
				OutputTexture.SetPixels(Pixels);
				OutputTexture.Apply(Mipmap, false);
				return OutputTexture;
			}
			catch
			{
				DestroyTemporaryTexture(OutputTexture);
				throw;
			}
			finally
			{
				RenderTexture.active = OriginActiveRenderTexture;
				DestroyTemporaryTexture(ReadbackTexture);
			}
		}

		private static void DestroyTemporaryTexture(Texture2D Texture)
		{
			if (Texture == null)
				return;
			if (Application.isPlaying)
				Object.Destroy(Texture);
			else
				Object.DestroyImmediate(Texture);
		}

		public static void ClearRenderTexture(RenderTexture SourceRenderTexture, Color ClearColor)
		{
			RenderTexture OriginActiveRenderTexture = RenderTexture.active;
			try
			{
				RenderTexture.active = SourceRenderTexture;
				GL.Clear(true, true, ClearColor);
			}
			finally
			{
				RenderTexture.active = OriginActiveRenderTexture;
			}
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////
		/// Buffer Allocate Functions |
		////////////////////////////////////////////////////////////////////////////////////////////////////

		public static T[] NewArray<T>(int Count)
		{
			return new T[Count];
		}

		public static T[] NewArray<T>(int Count, T InitializeValue)
		{
			T[] Buffer = new T[Count];
			for (int ElementID = 0; ElementID < Count; ElementID++)
				Buffer[ElementID] = InitializeValue;
			return Buffer;
		}

		public static ComputeBuffer CreateComputeBuffer<T>(int Capacity, int Size, ComputeBufferType ComputeBufferType = ComputeBufferType.Default)
		{
			var NewComputeBuffer = new ComputeBuffer(Capacity, Size, ComputeBufferType);
			return NewComputeBuffer;
		}

		public static ComputeBuffer CreateComputeBufferFromData<T>(T[] SourceData, int Size, ComputeBufferType ComputeBufferType = ComputeBufferType.Default)
		{
			var NewComputeBuffer = new ComputeBuffer(SourceData.Length, Size, ComputeBufferType);
			NewComputeBuffer.SetData(SourceData);
			return NewComputeBuffer;
		}

		public static void ReleaseBuffer(ComputeBuffer Buffer)
		{
			if (Buffer != null)
				Buffer.Release();
		}

		public static void ReleaseBuffer(ref ComputeBuffer Buffer)
		{
			if (Buffer != null)
				Buffer.Release();
			Buffer = null;
		}

		public static Cubemap CreateSolidCubemap(int Resolution, TextureFormat Format, bool Mipmap, Color SolidColor)
		{
			Color[] Colors = NewArray(Resolution * Resolution, SolidColor);
			Cubemap NewCubemap = new Cubemap(Resolution, Format, Mipmap);
			foreach (var EnumValue in System.Enum.GetValues(typeof(CubemapFace)))
			{
				CubemapFace CubemapFaceType = (CubemapFace)EnumValue;
				if (CubemapFaceType != CubemapFace.Unknown)
					NewCubemap.SetPixels(Colors, CubemapFaceType);
			}
			NewCubemap.Apply();

			return NewCubemap;
		}
	}
}
