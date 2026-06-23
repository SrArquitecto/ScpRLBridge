using Exiled.API.Features;
using ScpAgent.Bot.Sensors.Data;
using UnityEngine;
using System;
using Exiled.API.Enums;

namespace ScpAgent.Bot.Sensors.Modules
{
    public class DamageModule : ISensorDamageModule
    {
        private Player _player;
        private float   _pendingDamage     = 0f;
        private string  _pendingDamageType = "Unknown";
        private Vector3 _pendingDamageDir  = Vector3.zero; // dirección hacia el atacante
        private bool    _attackerInMemory  = false;
        private const float DAMAGE_DECAY = 0.5f; // segundos que persiste la info de daño
        private float   _lastDamageTime = -999f;
        private readonly InventoryItemData[] _inventoryPool = new InventoryItemData[8];
        public DamageModule()
        {
            for (int i = 0; i < _inventoryPool.Length;  i++) _inventoryPool[i]  = new InventoryItemData();
        }
        public void VincularPlayer(Player player)
        {
            _player = player;
        }
        public void Reset()
        {
            _pendingDamage     = 0f;
            _pendingDamageType = "Unknown";
            _pendingDamageDir  = Vector3.zero;
            _attackerInMemory  = false;
            _lastDamageTime    = -999f;

        }
        public void Actualizar(AgentObservation obs, SensorContext ctx)
        {
            _CargarDaño(obs);
        }

        private void _CargarDaño(AgentObservation obs)
        {
            float tiempoDesdeUltimoDaño = Time.time - _lastDamageTime;
        
            if (_pendingDamage > 0f && tiempoDesdeUltimoDaño < DAMAGE_DECAY)
            {
                float maxHealth = _player?.MaxHealth ?? 100f;
                obs.DamageReceived   = Mathf.Clamp01(_pendingDamage / maxHealth);
                obs.DamageType       = _pendingDamageType;
                obs.DamageDirX       = _pendingDamageDir.x;
                obs.DamageDirZ       = _pendingDamageDir.z;
                obs.AttackerInMemory = _attackerInMemory;
            }
            else
            {
                // Sin daño reciente — resetear a cero
                obs.DamageReceived   = 0f;
                obs.DamageType       = "None";
                obs.DamageDirX       = 0f;
                obs.DamageDirZ       = 0f;
                obs.AttackerInMemory = false;
                _pendingDamage       = 0f; // limpiar acumulado
            }
        }

        public void RegistrarDaño(float cantidad, string tipo, Vector3 dirHaciaAtacante, bool atacanteEnMemoria)
        {
            _pendingDamage     += cantidad; // acumular si hay varios hits en el mismo tick
            _pendingDamageType  = tipo;
            _pendingDamageDir   = dirHaciaAtacante;
            _attackerInMemory   = atacanteEnMemoria;
            _lastDamageTime     = Time.time;
        }

    }
}