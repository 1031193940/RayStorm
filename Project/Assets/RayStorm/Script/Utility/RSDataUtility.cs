using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
		var NewTexture2D = new Texture2D(SourceRenderTexture.width, SourceRenderTexture.height, Format, Mipmap);

		var OriginActiveRenderTexture = RenderTexture.active;
		{
			RenderTexture.active = SourceRenderTexture;
			NewTexture2D.ReadPixels(new Rect(0, 0, SourceRenderTexture.width, SourceRenderTexture.height), 0, 0);
			NewTexture2D.Apply();
		}
		RenderTexture.active = OriginActiveRenderTexture;

		return NewTexture2D;
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

		return NewCubemap;
	}
}
