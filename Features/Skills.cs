using System.Globalization;
using EFT;
using EFT.Trainer.Configuration;
using EFT.Trainer.Extensions;
using EFT.Trainer.Properties;
using JetBrains.Annotations;
using UnityEngine;

#nullable enable

namespace EFT.Trainer.Features;

[UsedImplicitly]
internal class Skills : TriggerFeature
{
	private const float DefaultXpMultiplier = 1f;

	private string _xpMultiplierText = "1";
	private float _xpMultiplierValue = DefaultXpMultiplier;

	public override string Name => Strings.FeatureSkillsName;
	public override string Description => Strings.FeatureSkillsDescription;

	// Original-Hotkey bleibt bestehen.
	[ConfigurationProperty(Order = 1)]
	public override KeyCode Key { get; set; } = KeyCode.None;

	[ConfigurationProperty(Order = 10)]
	public bool DisableXpPenalty { get; set; } = true;

	[ConfigurationProperty(Order = 11)]
	public bool EnableXpMultiplier { get; set; } = false;

	[ConfigurationProperty(Order = 12)]
	public string XpMultiplier
	{
		get => _xpMultiplierText;
		set => SetXpMultiplier(value);
	}

	// Originale Skills-Logik:
	// Wird einmal ausgeführt, wenn der konfigurierte Hotkey gedrückt wird.
	protected override void UpdateOnceWhenTriggered()
	{
		var player = GameState.Current?.LocalPlayer;
		if (!player.IsValid())
			return;

		if (player.Skills?.Skills == null)
			return;

		foreach (var skill in player.Skills.DisplayList)
			skill.SetLevel(51);

		if (player.Skills.Mastering == null)
			return;

		foreach (var item in player.Skills.Mastering.Values)
			item.Current = item.MasteringGroup.Level2 + item.MasteringGroup.Level3;
	}

	// Zusätzliche Modifier-Logik:
	// Läuft unabhängig vom Hotkey, damit Penalty/Multiplier dauerhaft wirken können.
	[UsedImplicitly]
	private void LateUpdate()
	{
		var player = GameState.Current?.LocalPlayer;
		if (!player.IsValid())
			return;

		if (player.Skills == null)
			return;

		HarmonyPatchOnce(harmony =>
		{
			HarmonyPostfix(
				harmony,
				typeof(SkillManager),
				nameof(SkillManager.GetEffectiveness),
				nameof(GetEffectivenessPostfix),
				new[] { typeof(int) }
			);

			HarmonyPrefix(
				harmony,
				typeof(global::SkillClass),
				nameof(global::SkillClass.UseEffectiveness),
				nameof(UseEffectivenessPrefix),
				new[] { typeof(float) }
			);

			HarmonyPrefix(
				harmony,
				typeof(global::SkillClass),
				nameof(global::SkillClass.OnTrigger),
				nameof(OnTriggerPrefix),
				new[] { typeof(SkillManager.SkillActionClass), typeof(float) }
			);
		});

		if (!DisableXpPenalty)
			return;

		foreach (var skill in player.Skills.Skills)
		{
			if (skill == null)
				continue;

			if (skill.Float_3 < 1f)
				skill.Float_3 = 1f;
		}
	}

	private void SetXpMultiplier(string? value)
	{
		var normalized = NormalizeMultiplierInput(value);

		if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
		{
			SetDefaultMultiplier();
			return;
		}

		if (float.IsNaN(parsed) || float.IsInfinity(parsed))
		{
			SetDefaultMultiplier();
			return;
		}

		if (parsed < 0f)
		{
			SetDefaultMultiplier();
			return;
		}

		_xpMultiplierText = normalized;
		_xpMultiplierValue = parsed;
	}

	private static string NormalizeMultiplierInput(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return "1";

		var text = value.Trim().Replace(",", ".");
		var hasDecimalSeparator = false;

		foreach (var c in text)
		{
			if (char.IsDigit(c))
				continue;

			if (c == '.' && !hasDecimalSeparator)
			{
				hasDecimalSeparator = true;
				continue;
			}

			// Buchstaben oder andere ungültige Zeichen:
			// zurück auf Vanilla 1x.
			return "1";
		}

		return text;
	}

	private void SetDefaultMultiplier()
	{
		_xpMultiplierText = "1";
		_xpMultiplierValue = DefaultXpMultiplier;
	}

	private float GetEffectiveXpMultiplier()
	{
		if (!EnableXpMultiplier)
			return DefaultXpMultiplier;

		if (_xpMultiplierValue < 0f || float.IsNaN(_xpMultiplierValue) || float.IsInfinity(_xpMultiplierValue))
			return DefaultXpMultiplier;

		return _xpMultiplierValue;
	}

	[UsedImplicitly]
	private static void GetEffectivenessPostfix(SkillManager __instance, ref float __result)
	{
		var feature = FeatureFactory.GetFeature<Skills>();
		if (feature == null || !feature.DisableXpPenalty)
			return;

		if (!IsLocalSkillManager(__instance))
			return;

		// Fresh-Bonus über 1.0 bleibt erhalten.
		// Nur XP-Fatigue/Penalty unter 1.0 wird verhindert.
		if (__result < 1f)
			__result = 1f;
	}

	[UsedImplicitly]
	private static void UseEffectivenessPrefix(global::SkillClass __instance)
	{
		var feature = FeatureFactory.GetFeature<Skills>();
		if (feature == null || !feature.DisableXpPenalty)
			return;

		if (!IsLocalSkillManager(__instance.SkillManager))
			return;

		if (__instance.Float_3 < 1f)
			__instance.Float_3 = 1f;
	}

	[UsedImplicitly]
	private static void OnTriggerPrefix(global::SkillClass __instance, ref float val)
	{
		var feature = FeatureFactory.GetFeature<Skills>();
		if (feature == null || !feature.EnableXpMultiplier)
			return;

		if (!IsLocalSkillManager(__instance.SkillManager))
			return;

		val *= feature.GetEffectiveXpMultiplier();
	}

	private static bool IsLocalSkillManager(SkillManager skillManager)
	{
		var player = GameState.Current?.LocalPlayer;
		if (!player.IsValid())
			return false;

		return ReferenceEquals(player.Skills, skillManager);
	}
}
