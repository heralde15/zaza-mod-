using System;
using System.Drawing;
using System.Numerics;
using Microsoft.Xna.Framework;
using Color = Microsoft.Xna.Framework.Color;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Graphics;

namespace Terraria.DataStructures;

public class SpriteDrawBuffer
{
	private readonly GraphicsDevice graphicsDevice;

	private readonly SpriteBatch spriteBatch;

	private readonly int bufferSize;

	private DynamicVertexBuffer vertexBuffer;

	private IndexBuffer indexBuffer;

	private int vertexCount;

	private VertexPositionColorTexture[] vertices;

	private Texture[] textures;

	private int uploadedSpriteIndex;

	private VertexBufferBinding[] preBindVertexBuffers;

	private IndexBuffer preBindIndexBuffer;

	public SpriteDrawBuffer(GraphicsDevice graphicsDevice, int bufferSize = 2048)
	{
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Expected O, but got Unknown
		uploadedSpriteIndex = -1;
		base._002Ector();
		this.graphicsDevice = graphicsDevice;
		this.bufferSize = bufferSize;
		ResizeArrays(bufferSize);
		spriteBatch = new SpriteBatch(graphicsDevice);
	}

	public void ResizeArrays(int count)
	{
		Array.Resize(ref vertices, count * 4);
		Array.Resize(ref textures, count);
	}

	public void ApplyDefaultSpriteEffect(RasterizerState rasterizer, Matrix4x4 transformation)
	{
		spriteBatch.Begin((SpriteSortMode)0, (BlendState)null, (SamplerState)null, (DepthStencilState)null, rasterizer, (Effect)null, transformation);
		spriteBatch.End();
	}

	public void ApplyDefaultSpriteEffect()
	{
		spriteBatch.Begin();
		spriteBatch.End();
	}

	private void CheckBuffers()
	{
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Expected O, but got Unknown
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Expected O, but got Unknown
		if (vertexBuffer == null || ((GraphicsResource)vertexBuffer).IsDisposed)
		{
			if (vertexBuffer != null)
			{
				((GraphicsResource)vertexBuffer).Dispose();
			}
			vertexBuffer = new DynamicVertexBuffer(graphicsDevice, typeof(VertexPositionColorTexture), bufferSize * 4, (BufferUsage)1);
		}
		if (indexBuffer == null || ((GraphicsResource)indexBuffer).IsDisposed)
		{
			if (indexBuffer != null)
			{
				((GraphicsResource)indexBuffer).Dispose();
			}
			indexBuffer = new IndexBuffer(graphicsDevice, typeof(ushort), bufferSize * 6, (BufferUsage)1);
			indexBuffer.SetData<ushort>(GenIndexBuffer(bufferSize));
		}
	}

	private static ushort[] GenIndexBuffer(int maxSprites)
	{
		ushort[] array = new ushort[maxSprites * 6];
		int num = 0;
		ushort num2 = 0;
		while (num < maxSprites)
		{
			array[num++] = num2;
			array[num++] = (ushort)(num2 + 1);
			array[num++] = (ushort)(num2 + 2);
			array[num++] = (ushort)(num2 + 3);
			array[num++] = (ushort)(num2 + 2);
			array[num++] = (ushort)(num2 + 1);
			num2 += 4;
		}
		return array;
	}

	private void Bind()
	{
		if (preBindVertexBuffers == null)
		{
			preBindVertexBuffers = graphicsDevice.GetVertexBuffers();
			preBindIndexBuffer = graphicsDevice.Indices;
			graphicsDevice.SetVertexBuffer((VertexBuffer)(object)vertexBuffer);
			graphicsDevice.Indices = indexBuffer;
		}
	}

	public void Unbind()
	{
		if (preBindVertexBuffers != null)
		{
			graphicsDevice.SetVertexBuffers(preBindVertexBuffers);
			graphicsDevice.Indices = preBindIndexBuffer;
			preBindVertexBuffers = null;
			preBindIndexBuffer = null;
		}
	}

	public int DrawRange(int index, int count)
	{
		vertexCount = 0;
		CheckBuffers();
		Bind();
		graphicsDevice.Textures[0] = textures[index];
		int num = 0;
		while (count > 0)
		{
			if (uploadedSpriteIndex < 0 || index < uploadedSpriteIndex || index + count > uploadedSpriteIndex + bufferSize)
			{
				vertexBuffer.SetData<VertexPositionColorTexture>(vertices, index * 4, Math.Min(vertices.Length - index * 4, bufferSize * 4), (SetDataOptions)1);
				uploadedSpriteIndex = index;
			}
			int num2 = Math.Min(count, bufferSize);
			int num3 = index - uploadedSpriteIndex;
			graphicsDevice.DrawIndexedPrimitives((PrimitiveType)0, num3 * 4, 0, num2 * 4, 0, num2 * 2);
			count -= num2;
			index += num2;
			num++;
		}
		return num;
	}

	public void DrawSingle(int index)
	{
		DrawRange(index, 1);
	}

	public int DrawAll()
	{
		if (vertexCount == 0)
		{
			return 0;
		}
		int num = vertexCount / 4;
		Texture texture = textures[0];
		int num2 = 0;
		int num3 = 0;
		for (int i = 1; i < num; i++)
		{
			Texture texture2 = textures[i];
			if (texture2 != texture)
			{
				num3 += DrawRange(num2, i - num2);
				num2 = i;
				texture = texture2;
			}
		}
		num3 += DrawRange(num2, num - num2);
		Unbind();
		return num3;
	}

	public void Draw(Texture2D texture, Vector2 position, VertexColors colors)
	{
		Draw(texture, position, null, colors, 0f, Vector2.Zero, 1f, (SpriteEffects)0);
	}

	public void Draw(Texture2D texture, Rectangle destination, VertexColors colors)
	{
		Draw(texture, destination, null, colors);
	}

	public void Draw(Texture2D texture, Rectangle destination, Rectangle? sourceRectangle, VertexColors colors)
	{
		Draw(texture, destination, sourceRectangle, colors, 0f, Vector2.Zero, (SpriteEffects)0);
	}

	public void Draw(Texture2D texture, Vector2 position, Rectangle? sourceRectangle, VertexColors color, float rotation, Vector2 origin, float scale, SpriteEffects effects)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		Draw(texture, position, sourceRectangle, color, rotation, origin, new Vector2(scale, scale), effects);
	}

	public void Draw(Texture2D texture, Vector2 position, Rectangle? sourceRectangle, VertexColors colors, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects)
	{
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		float z;
		float w;
		if (sourceRectangle.HasValue)
		{
			z = (float)sourceRectangle.Value.Width * scale.X;
			w = (float)sourceRectangle.Value.Height * scale.Y;
		}
		else
		{
			z = (float)texture.Width * scale.X;
			w = (float)texture.Height * scale.Y;
		}
		Draw(texture, new Vector4(position.X, position.Y, z, w), sourceRectangle, colors, rotation, origin, effects);
	}

	public void Draw(Texture2D texture, Rectangle destination, Rectangle? sourceRectangle, VertexColors colors, float rotation, Vector2 origin, SpriteEffects effects)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		Draw(texture, new Vector4(destination.X, destination.Y, destination.Width, destination.Height), sourceRectangle, colors, rotation, origin, effects);
	}

	public void Draw(Texture2D texture, Vector4 destination, VertexColors colors, float rotation = 0f, Vector2 origin = default(Vector2), SpriteEffects effects = (SpriteEffects)0)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		Draw(texture, destination, null, colors, rotation, origin, effects);
	}

	public void Draw(Texture2D texture, Vector4 destinationRectangle, Rectangle? sourceRectangle, VertexColors colors, float rotation = 0f, Vector2 origin = default(Vector2), SpriteEffects effect = (SpriteEffects)0, float depth = 0f)
	{
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		Vector4 sourceRectangle2 = default(Vector4);
		if (sourceRectangle.HasValue)
		{
			sourceRectangle2.X = sourceRectangle.Value.X;
			sourceRectangle2.Y = sourceRectangle.Value.Y;
			sourceRectangle2.Z = sourceRectangle.Value.Width;
			sourceRectangle2.W = sourceRectangle.Value.Height;
		}
		else
		{
			sourceRectangle2.X = 0f;
			sourceRectangle2.Y = 0f;
			sourceRectangle2.Z = texture.Width;
			sourceRectangle2.W = texture.Height;
		}
		Vector2 texCoordTL = new Vector2
		{
			X = sourceRectangle2.X / (float)texture.Width,
			Y = sourceRectangle2.Y / (float)texture.Height
		};
		Vector2 texCoordBR = new Vector2
		{
			X = (sourceRectangle2.X + sourceRectangle2.Z) / (float)texture.Width,
			Y = (sourceRectangle2.Y + sourceRectangle2.W) / (float)texture.Height
		};
		if ((effect & 2) != 0)
		{
			float y = texCoordBR.Y;
			texCoordBR.Y = texCoordTL.Y;
			texCoordTL.Y = y;
		}
		if ((effect & 1) != 0)
		{
			float x = texCoordBR.X;
			texCoordBR.X = texCoordTL.X;
			texCoordTL.X = x;
		}
		QueueSprite(destinationRectangle, -origin, colors, sourceRectangle2, texCoordTL, texCoordBR, texture, depth, rotation);
	}

	private void QueueSprite(Vector4 destinationRect, Vector2 origin, VertexColors colors, Vector4 sourceRectangle, Vector2 texCoordTL, Vector2 texCoordBR, Texture2D texture, float depth, float rotation)
	{
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		uploadedSpriteIndex = -1;
		float num = origin.X / sourceRectangle.Z;
		float num2 = origin.Y / sourceRectangle.W;
		float x = destinationRect.X;
		float y = destinationRect.Y;
		float z = destinationRect.Z;
		float w = destinationRect.W;
		float num3 = num * z;
		float num4 = num2 * w;
		float num5;
		float num6;
		if (rotation != 0f)
		{
			num5 = (float)Math.Cos(rotation);
			num6 = (float)Math.Sin(rotation);
		}
		else
		{
			num5 = 1f;
			num6 = 0f;
		}
		int num7 = vertexCount / 4;
		if (num7 >= textures.Length)
		{
			ResizeArrays(textures.Length * 2);
		}
		textures[num7] = (Texture)(object)texture;
		PushVertex(new Vector3(x + num3 * num5 - num4 * num6, y + num3 * num6 + num4 * num5, depth), colors.TopLeftColor, texCoordTL);
		PushVertex(new Vector3(x + (num3 + z) * num5 - num4 * num6, y + (num3 + z) * num6 + num4 * num5, depth), colors.TopRightColor, new Vector2(texCoordBR.X, texCoordTL.Y));
		PushVertex(new Vector3(x + num3 * num5 - (num4 + w) * num6, y + num3 * num6 + (num4 + w) * num5, depth), colors.BottomLeftColor, new Vector2(texCoordTL.X, texCoordBR.Y));
		PushVertex(new Vector3(x + (num3 + z) * num5 - (num4 + w) * num6, y + (num3 + z) * num6 + (num4 + w) * num5, depth), colors.BottomRightColor, texCoordBR);
	}

	private void PushVertex(Vector3 pos, Color color, Vector2 texCoord)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		SetVertex(ref vertices[vertexCount++], pos, color, texCoord);
	}

	private static void SetVertex(ref VertexPositionColorTexture vertex, Vector3 pos, Color color, Vector2 texCoord)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Unknown result type (might be due to invalid IL or missing references)
		vertex.Position = pos;
		vertex.Color = color;
		vertex.TextureCoordinate = texCoord;
	}
}
