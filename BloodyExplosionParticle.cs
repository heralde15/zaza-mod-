using System.Drawing;
using System.Numerics;
using Microsoft.Xna.Framework;
using Color = Microsoft.Xna.Framework.Color;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria.UtilClient.Features;

namespace Terraria.Graphics.Renderers;

public class BloodyExplosionParticle : ABasicParticle
{
	public float FadeInNormalizedTime;

	public float FadeOutNormalizedTime;

	public float TimeToLive;

	public float Opacity;

	public float InnerOpacity;

	public float InitialScale;

	public Color ColorTint;

	public Color LightColorTint;

	private float _timeSinceSpawn;

	public override void FetchFromPool()
	{
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		base.FetchFromPool();
		_timeSinceSpawn = 0f;
		Opacity = 0f;
		InnerOpacity = 0f;
		FadeInNormalizedTime = 0.1f;
		FadeOutNormalizedTime = 0.9f;
		TimeToLive = 20f;
		InitialScale = 1f;
		ColorTint = Color.White;
		LightColorTint = Color.Transparent;
	}

	public override void Update(ref ParticleRendererSettings settings)
	{
		//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_018b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		base.Update(ref settings);
		_timeSinceSpawn++;
		float fromValue = _timeSinceSpawn / TimeToLive;
		Scale = Vector2.One * InitialScale * Utils.Remap(fromValue, 0f, 0.3f, 0.5f, 1f);
		float num = 0.4f;
		Opacity = MathHelper.Clamp(Utils.Remap(fromValue, 0f, FadeInNormalizedTime, 0f, 1f) * Utils.Remap(fromValue, FadeOutNormalizedTime, 1f, 1f, 0f), 0f, 1f) * num;
		InnerOpacity = MathHelper.Clamp(Utils.Remap(fromValue, 0f, FadeInNormalizedTime * 0.75f, 0f, 1f) * Utils.Remap(fromValue, 0.3f, 0.45f, 1f, 0f), 0f, 1f) * num;
		if (_timeSinceSpawn == 3f)
		{
			Rectangle r = Utils.CenteredRectangle(LocalPosition, new Vector2(16f, 16f));
			for (int i = 0; i < 50; i++)
			{
				Vector2 velocity = Main.rand.NextVector2CircularEdge(4f, 4f);
				if (i % 2 == 0)
				{
					velocity *= 0.5f;
				}
				Dust obj = Main.dust[Dust.NewDust(r.TopLeft(), r.Width, r.Height, 5, 0f, 0f, 100, default(Color), 1.25f + Main.rand.NextFloat() * 0.5f)];
				obj.velocity = velocity;
				obj.noGravity = i % 3 == 0;
			}
		}
		if (LightColorTint != Color.Transparent)
		{
			Color color = LightColorTint * Opacity;
			Lighting.AddLight(LocalPosition, (float)(int)color.R / 255f, (float)(int)color.G / 255f, (float)(int)color.B / 255f);
		}
		if (_timeSinceSpawn >= TimeToLive)
		{
			base.ShouldBeRemovedFromRenderer = true;
		}
	}

	public override void Draw(ref ParticleRendererSettings settings, SpriteBatch spritebatch)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		if (!Culling.ShouldCull(LocalPosition))
		{
			float num = _timeSinceSpawn / TimeToLive;
			Vector2 position = settings.AnchorPosition + LocalPosition;
			Color val = Color.Lerp(Lighting.GetColor(LocalPosition.ToTileCoordinates()).MultiplyRGBA(ColorTint), ColorTint, 0.65f);
			Texture2D value = TextureAssets.Extra[174].Value;
			Vector2 origin = new Vector2(value.Width / 2, value.Height / 2);
			Vector2 scale = Scale * (1.1f + 0.15f * num);
			Color color2 = val * Opacity;
			Texture2D value2 = TextureAssets.Extra[267].Value;
			Vector2 origin2 = new Vector2(value2.Width / 2, value2.Height / 2);
			Vector2 scale2 = Scale * (1f + 0.05f * num);
			Color color3 = val * InnerOpacity;
			spritebatch.Draw(value, position, (Rectangle?)value.Frame(), color2, Rotation, origin, scale, (SpriteEffects)0, 0f);
			spritebatch.Draw(value2, position, (Rectangle?)value2.Frame(), color3, Rotation, origin2, scale2, (SpriteEffects)0, 0f);
		}
	}

	public BloodyExplosionParticle()
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		FadeInNormalizedTime = 0.25f;
		FadeOutNormalizedTime = 0.75f;
		TimeToLive = 20f;
		InitialScale = 1f;
		ColorTint = Color.White;
		LightColorTint = Color.Transparent;
		base._002Ector();
	}
}
