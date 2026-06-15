using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class OilFireReaction : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
            var energyReleased = 0f;
            var oldHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
            var temperature = mixture.Temperature;
            var location = holder as TileAtmosphere;
            mixture.ReactionResults[(byte)GasReaction.Fire] = 0;

            // Нефть горит при высоких температурах, как и плазма
            var temperatureScale = 0f;

            if (temperature > Atmospherics.OilUpperTemperature)
                temperatureScale = 1f;
            else
            {
                temperatureScale = (temperature - Atmospherics.OilMinimumBurnTemperature) /
                                   (Atmospherics.OilUpperTemperature - Atmospherics.OilMinimumBurnTemperature);
            }

            if (temperatureScale > 0)
            {
                var oxygenBurnRate = Atmospherics.OxygenBurnRateBase - temperatureScale;
                var oilBurnRate = 0f;

                var initialOxygenMoles = mixture.GetMoles(Gas.Oxygen);
                var initialOilMoles = mixture.GetMoles(Gas.Oil);

                if (initialOxygenMoles > initialOilMoles * Atmospherics.OilOxygenFullburn)
                    oilBurnRate = initialOilMoles * temperatureScale / Atmospherics.OilBurnRateDelta;
                else
                    oilBurnRate = temperatureScale * (initialOxygenMoles / Atmospherics.OilOxygenFullburn) / Atmospherics.OilBurnRateDelta;

                if (oilBurnRate > Atmospherics.MinimumHeatCapacity)
                {
                    oilBurnRate = MathF.Min(oilBurnRate, MathF.Min(initialOilMoles, initialOxygenMoles / oxygenBurnRate));
                    mixture.SetMoles(Gas.Oil, initialOilMoles - oilBurnRate);
                    mixture.SetMoles(Gas.Oxygen, initialOxygenMoles - oilBurnRate * oxygenBurnRate);

                    // При сгорании нефти: 80% CO2 и 20% водяного пара
                    mixture.AdjustMoles(Gas.CarbonDioxide, oilBurnRate * 0.8f);
                    mixture.AdjustMoles(Gas.WaterVapor, oilBurnRate * 0.2f);

                    energyReleased += Atmospherics.FireOilEnergyReleased * oilBurnRate;
                    energyReleased /= heatScale;
                    mixture.ReactionResults[(byte)GasReaction.Fire] += oilBurnRate * (1 + oxygenBurnRate);
                }
            }

            if (energyReleased > 0)
            {
                var newHeatCapacity = atmosphereSystem.GetHeatCapacity(mixture, true);
                if (newHeatCapacity > Atmospherics.MinimumHeatCapacity)
                    mixture.Temperature = (temperature * oldHeatCapacity + energyReleased) / newHeatCapacity;
            }

            if (location != null)
            {
                var mixTemperature = mixture.Temperature;
                if (mixTemperature > Atmospherics.FireMinimumTemperatureToExist)
                {
                    atmosphereSystem.HotspotExpose(location, mixTemperature, mixture.Volume);
                }
            }

            return mixture.ReactionResults[(byte)GasReaction.Fire] != 0 ? ReactionResult.Reacting : ReactionResult.NoReaction;
        }
    }
}
