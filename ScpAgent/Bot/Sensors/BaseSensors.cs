using System;
using System.Collections.Generic;
using UnityEngine;
using Exiled.API.Features;
using ScpAgent.Bot.Data;
using Exiled.API.Features.Doors;
using ScpAgent.Bot.Sensors.Intefaces;
using PlayerRoles;
using Exiled.API.Enums;
using ScpAgent.Bot.Sensors.Modules.Memory;
using ScpAgent.Bot.Sensors.Data;
using ScpAgent.Bot.Sensors.Modules.Memory.Data;
using ScpAgent.Bot.Sensors.Modules;
using PlayerRoles.PlayableScps.Scp079.Rewards;
using System.Runtime.InteropServices.ComTypes;


namespace ScpAgent.Bot.Sensors
{
    public abstract class BaseSensors : ISensors
    {
        // ── Player — NO readonly para poder actualizar tras respawn ────────────
        protected Player _player;
        protected int _agentId;
        public static readonly AgentObservation obsVacia = new AgentObservation { Done = true };
        private readonly AgentObservation _obsCache = new AgentObservation();

        // ── Estado de movimiento ────────────────────────────────────────────────
        protected Vector3 _lastPos;
        protected float   _lastYaw;
        protected float   _lastPitch;


        // ── Cache de datos de sala por agente ──────────────────────────────────
        public static Dictionary<int, AgentCacheData> agentCacheData = new Dictionary<int, AgentCacheData>();
        protected static readonly AgentCacheData _fallbackCacheData = new AgentCacheData 
        { 
            center = Vector3.zero,
            halfX = Vector3.one.x * 10f,
            halfY = Vector3.one.y * 10f,
            halfZ = Vector3.one.z * 10f,
        };

       
        //ESTRATEGIAS:
        protected Func<ItemType, float> _fnPrioridad;
        protected Func<ItemType, string> _fnCategoria;


        protected readonly List<ISensorModule> _modules = new List<ISensorModule>();

        //MODULOS DE SENSORES
        protected readonly ISensorPlayerModule  _players        = new PlayerVisionModule();
        protected readonly ISensorDamageModule  _damage         = new DamageModule();
        protected readonly ISensorRoomModule    _rooms          = new RoomsModule();
        protected readonly ISensorModule        _doors          = new DoorsModule();
        protected readonly ISensorModule        _lifts          = new LiftsModule();
        protected readonly ISensorVelocityModule        _velocity       = new VelocityModule();
        protected readonly ISensorModule        _whiskers       = new WhiskersModule();
        protected readonly ISensorModule        _basic          = new BasicPlayerModule();
        protected readonly ISensorModule        _aim            = new AimModule();

        // ───────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ───────────────────────────────────────────────────────────────────────
        public BaseSensors(int agentId)
        {
            _agentId = agentId;
        }

        public virtual void Init()
        {
            _modules.Add(_players);
            _modules.Add(_damage);
            _modules.Add(_rooms);
            _modules.Add(_doors);
            _modules.Add(_lifts);
            _modules.Add(_velocity);
            _modules.Add(_whiskers);
            _modules.Add(_basic);
            _modules.Add(_aim);
        }

        /// <summary>
        /// Actualiza la referencia al jugador tras un respawn sin recrear la instancia.
        /// Preserva las cachés de listas globales (puertas, lifts, keycards).
        /// </summary>
        public void VincularPlayer(Player freshPlayer)
        {
            _player = freshPlayer;

            // Actualizar posición base con la nueva posición de spawn
            if (_player != null)
            {
                _velocity.SetLastPos(_player.Position);
                _velocity.SetLastYaw(_player.CameraTransform.rotation.eulerAngles.y);
                _velocity.SetLastPitch(_player.CameraTransform.rotation.eulerAngles.x);
            }
            foreach (var m in _modules) m.VincularPlayer(_player);

            Log.Debug($"[AgentSensors] Player vinculado: {freshPlayer?.Nickname}");
        }

        /// <summary>
        /// Invalida las cachés de objetos del mapa (llamar tras Round.Restart).
        /// </summary>

        // ───────────────────────────────────────────────────────────────────────
        // MÉTODO PRINCIPAL
        // ───────────────────────────────────────────────────────────────────────
        public virtual AgentObservation GetCurrentState(
            float delta, int accionAnterior, float reward, bool done, RoleTypeId role, int playerTier)
        {
            if (_player == null || !_player.IsAlive || _player.GameObject == null)
                return obsVacia;
            if (_player.CameraTransform == null)
                return obsVacia;

            Vector3 pos         = _player.Position;
            Vector3 camRotation = _player.CameraTransform.rotation.eulerAngles;
            var obs = _obsCache;
            var data = GetData();
            SensorContext ctx = _BuildContext(delta, reward, accionAnterior, data, done);

            foreach (var module in _modules)
                module.Actualizar(obs, ctx);

            return obs;
        }

        private SensorContext _BuildContext(float deltaTime, float reward, int lastAction, AgentCacheData data, bool done)
        {
            return new SensorContext
            {
                HalfX       = data.halfX,
                HalfY       = data.halfY,
                HalfZ       = data.halfZ,
                Center      = data.center,
                Delta       = deltaTime,
                Reward      = reward,
                LastAction  = lastAction,
                Done        = done
            };
        }
        
        public void MarcarRoomDescubierta(Room sala)
        {
            _rooms.MarcarRoomDescubierta(sala);
        }

        // ───────────────────────────────────────────────────────────────────────
        // VELOCIDADES
        // ───────────────────────────────────────────────────────────────────────

        protected AgentCacheData GetData()
        {
            AgentCacheData data;
            if (_player == null || !agentCacheData.TryGetValue(_player.Id, out data) || !data.IsDataReady)
            {
                data = _fallbackCacheData;
            }
            return data;
        }

        public virtual void ResetEstado()
        {

            foreach (var m in _modules) m.Reset();
        }
        public abstract void VincularEstrategia(Func<ItemType, float> fnPrioridad, Func<ItemType, string> fnCategoria);
        
        public abstract void Destruir();

        public void RegistrarDaño(float cantidad, string tipo, Vector3 dirHaciaAtacante, bool atacanteEnMemoria)
        {
            _damage.RegistrarDaño(cantidad, tipo, dirHaciaAtacante, atacanteEnMemoria);
        }

        public bool TieneEnMemoriaJugadores(int playerId)
            => _players.TieneEnMemoria(playerId);
    }
}