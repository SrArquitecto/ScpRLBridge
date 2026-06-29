using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Exiled.API.Enums;
using Exiled.API.Features;
using System.Collections.Generic;
using Exiled.API.Features.Items;
using Exiled.API.Extensions;
using Mirror;
using NetworkManagerUtils.Dummies;
using InventorySystem.Items.Firearms.Modules;
using InventorySystem.Items.Firearms.ShotEvents;

namespace ScpAgent.Bot.Strategy.Movement
{
    /// <summary>
    /// Encapsula toda la lógica de movimiento físico de un bot humano.
    /// Recibe el GameObject y Player como parámetros — no guarda referencias
    /// que puedan quedar stale entre respawns.
    /// </summary>
    public class HumanMovement : BaseMovement
    {
        // ── Campos propios de BotMovement ────────────────────────────────────

        // ── Velocidades ──────────────────────────────────────────────────────
        private const float VEL_CAMINAR = 3.9f;
        private const float VEL_SPRINT  = 5.4052f;
        private const float VEL_CAMARA  = 15f;
        private const float VEL_CAMARA_PITCH = 7.5f;

        // ───────────────────────────────────────────────────────────────────
        // INICIALIZACIÓN
        // ───────────────────────────────────────────────────────────────────

        public HumanMovement(int agentId) : base(agentId)
        {
            
        }

        /// <summary>
        /// Inicializa CharacterController y MouseLook desde el GameObject.
        /// Llamar tras cada spawn/respawn cuando el cuerpo ya existe.
        /// </summary>


        // ───────────────────────────────────────────────────────────────────
        // EJECUCIÓN POR TICK
        // ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ejecuta la acción física. Recibe Player y GameObject como parámetros
        /// para evitar referencias stale entre respawns.
        /// </summary>



        // ───────────────────────────────────────────────────────────────────
        // MOVIMIENTO Y CÁMARA (privados)
        // ───────────────────────────────────────────────────────────────────

        protected override void _MoverPersonaje(int accion, float deltaTime, Player player, GameObject go)
        {
            if (_cc == null) return;

            float yawRad  = player.CameraTransform.rotation.eulerAngles.y * Mathf.Deg2Rad;
            Vector3 fwd   = new Vector3( Mathf.Sin(yawRad), 0f,  Mathf.Cos(yawRad)).normalized;
            Vector3 right = new Vector3( Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad)).normalized;

            Vector3 vel = accion switch
            {
                1 =>  fwd   * VEL_CAMINAR,
                2 => -fwd   * VEL_CAMINAR,
                3 => -right * VEL_CAMINAR,
                4 =>  right * VEL_CAMINAR,
                5 =>  fwd   * VEL_SPRINT,
                _ =>  Vector3.zero
            };

            vel.y = _cc.isGrounded ? -0.5f : -9.81f;
            _cc.Move(vel * deltaTime);

            // Sincronizar posición lógica de EXILED con Unity
            player.Position = go.transform.position;
        }


        public void EquiparTarjeta(Player player)
        {
            _EquiparTarjeta(player);
        }

        private void _EquiparTarjeta(Player player)
        {
            var item = player.Items.FirstOrDefault(
                i => i.Type.ToString().IndexOf("Keycard",
                    StringComparison.OrdinalIgnoreCase) >= 0);

            if (item != null) player.CurrentItem = item;
        }

        // ── Categorías de armas (clasificación principal / secundaria) ──────
        // NOTA: Se excluyen GunShotgun y GunRevolver porque su sistema de
        // disparo/recarga depende de protocolo cliente-servidor que los bots
        // con FakeConnection no pueden completar. Solo armas con AutomaticActionModule.
        private static readonly HashSet<ItemType> _armasPrimarias = new HashSet<ItemType>
        {
            ItemType.GunE11SR,     // rifle
            ItemType.GunLogicer,   // MG
            ItemType.GunAK,        // rifle
            ItemType.GunFRMG0,     // MG
            ItemType.GunA7,        // rifle
        };

        private static readonly HashSet<ItemType> _armasSecundarias = new HashSet<ItemType>
        {
            ItemType.GunCOM15,     // pistola
            ItemType.GunCOM18,     // pistola
            ItemType.GunCom45,     // pistola
            ItemType.GunFSP9,      // SMG
            ItemType.GunCrossvec,  // SMG
        };

        private static readonly HashSet<ItemType> _medicamentos = new HashSet<ItemType>
        {
            ItemType.Medkit,
            ItemType.Painkillers,
            ItemType.Adrenaline,
            ItemType.SCP500,
        };

        private static readonly HashSet<ItemType> _granadas = new HashSet<ItemType>
        {
            ItemType.GrenadeHE,
            ItemType.GrenadeFlash,
            ItemType.SCP018,
        };

        // Cualquier cosa que empiece por "Gun" o sea MicroHID/ParticleDisruptor
        private static bool _EsCualquierArma(ItemType t)
        {
            string s = t.ToString();
            return s.StartsWith("Gun") || s == "MicroHID" || s == "ParticleDisruptor";
        }

        private static bool _EsArmaPrincipal(ItemType t) => _EsCualquierArma(t) && !_armasSecundarias.Contains(t);
        private static bool _EsArmaSecundaria(ItemType t) => _armasSecundarias.Contains(t);
        private static bool _EsMedicamento(ItemType t) => _medicamentos.Contains(t);
        private static bool _EsGranada(ItemType t) => _granadas.Contains(t);

        // ── Tirar item equipado ─────────────────────────────────────────────
        public void TirarItem(Player player)
        {
            _TirarItem(player);
        }

        private void _TirarItem(Player player)
        {
            try
            {
                //player.DropHeldItem();
                Log.Info("Tiro item");
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] TirarItem: {ex.Message}");
            }
        }

        // ── Equipar arma principal ──────────────────────────────────────────
        public void EquiparArmaPrincipal(Player player)
        {
            _EquiparPorCategoria(player, _EsArmaPrincipal, "arma principal");
        }

        // ── Equipar arma secundaria ────────────────────────────────────────
        public void EquiparArmaSecundaria(Player player)
        {
            _EquiparPorCategoria(player, _EsArmaSecundaria, "arma secundaria");
        }

        // ── Equipar medicamento ────────────────────────────────────────────
        public void EquiparMedicamento(Player player)
        {
            _EquiparPorCategoria(player, _EsMedicamento, "medicamento");
        }

        // ── Equipar granada ────────────────────────────────────────────────
        public void EquiparGranada(Player player)
        {
            _EquiparPorCategoria(player, _EsGranada, "granada");
        }

        private void _EquiparPorCategoria(Player player, Func<ItemType, bool> pred, string nombreCategoria)
        {
            if (player == null || player.Items == null) return;

            try
            {
                var item = player.Items.FirstOrDefault(i => i != null && pred(i.Type));
                if (item == null) return;

                // Para bots con FakeConnection, setear solo CurrentItem (que llama
                // ServerSelectItem) no basta: el SyncVar Mirror se envía a
                // FakeConnection.Send (vacío) y el render del viewmodel no se actualiza.
                // Forzamos la actualización local directa.
                player.Inventory.ServerSelectItem(item.Serial);
                if (item.Base != null)
                    player.Inventory.CurInstance = item.Base;
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] _EquiparPorCategoria({nombreCategoria}): {ex.Message}");
            }
        }

        // ── Recargar arma equipada ─────────────────────────────────────────
        public void RecargarArma(Player player)
        {
            _RecargarArma(player);
        }

        private void _RecargarArma(Player player)
        {
            try
            {
                if (!(player.CurrentItem?.Base is InventorySystem.Items.Firearms.Firearm baseFirearm)) return;

                // No recargar si ya está en proceso
                if (baseFirearm.TryGetModule<IReloaderModule>(out var reloader) && reloader.IsReloadingOrUnloading)
                    return;

                baseFirearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo);
                int maxAmmo = primaryAmmo?.AmmoMax ?? 0;
                int currentAmmo = primaryAmmo?.AmmoStored ?? 0;

                // ── Caso especial: revólver (RevolverClipReloaderModule) ───────
                // ServerTryReload abre el cilindro pero la transferencia de ammo
                // requiere confirmación del cliente, que el bot nunca envía.
                // Solución: rellenar el cilindro directamente + marcar no-recargando.
                if (baseFirearm.TryGetModule<RevolverClipReloaderModule>(out var revolverMod))
                {
                    _FillRevolver(revolverMod, primaryAmmo, maxAmmo, currentAmmo);
                    return;
                }

                // ── Caso especial: escopeta de bombeo / doble cañón ───────────
                // Las escopetas cargan shells al tubo + chambered en recámara.
                // El pump action necesita ServerCycleAction en PumpActionModule.
                object shotgunAction = null;
                if (baseFirearm.TryGetModule<PumpActionModule>(out var pumpMod))
                    shotgunAction = pumpMod;
                else if (baseFirearm.TryGetModule<DoubleActionModule>(out var doubleMod))
                    shotgunAction = doubleMod;
                if (shotgunAction != null)
                {
                    _FillShotgun(baseFirearm, primaryAmmo, maxAmmo, currentAmmo, shotgunAction);
                    return;
                }

                // Ya está lleno, nada que hacer
                if (currentAmmo >= maxAmmo) return;

                // Throttle: no intentar recargar más de 1 vez cada 0.45s
                if (Time.time < _nextReloadAttempt) return;
                _nextReloadAttempt = Time.time + 0.45f;

                // (1) Dummy action "Reload->Click" — puede no existir para este server
                bool fired = _ClickDummyAction(player, "Reload->Click");

                // (2) Fallback directo: ServerTryReload en el reloader module
                if (!fired && reloader != null)
                {
                    var mi = reloader.GetType().GetMethod("ServerTryReload",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    mi?.Invoke(reloader, null);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] RecargarArma: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Rellena el cilindro del revólver directamente (bypaseando el protocolo
        // cliente-servidor que requiere la confirmación del cliente).
        private static void _FillRevolver(RevolverClipReloaderModule revolver,
            IPrimaryAmmoContainerModule primaryAmmo, int maxAmmo, int currentAmmo)
        {
            try
            {
                // Rellenar el cilindro (incremento positivo = añadir balas)
                if (primaryAmmo != null && currentAmmo < maxAmmo)
                    primaryAmmo.ServerModifyAmmo(maxAmmo - currentAmmo);

                // Forzar el estado a "no recargando" — la animación ya empezó
                // y no podemos hacer que el cilindro se cierre por sí solo sin
                // la confirmación del cliente.
                var isReloadingProp = typeof(AnimatorReloaderModuleBase).GetProperty("IsReloading",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                isReloadingProp?.SetValue(revolver, false);
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] _FillRevolver: {ex.Message}");
            }
        }

        // Rellena el tubo de la escopeta y chamber una bala en la recámara.
        private static void _FillShotgun(InventorySystem.Items.Firearms.Firearm baseFirearm,
            IPrimaryAmmoContainerModule primaryAmmo, int maxAmmo, int currentAmmo, object actionModule)
        {
            try
            {
                // Rellenar el tubo de shells
                if (primaryAmmo != null && currentAmmo < maxAmmo)
                    primaryAmmo.ServerModifyAmmo(maxAmmo - currentAmmo);

                // Forzar estado a "no recargando"
                baseFirearm.TryGetModule<IReloaderModule>(out var reloader);
                if (reloader != null)
                {
                    var isReloadingProp = typeof(AnimatorReloaderModuleBase).GetProperty("IsReloading",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    isReloadingProp?.SetValue(reloader, false);
                }

                // Llamar al cycle del action module (PumpActionModule.ServerCycleAction
                // o DoubleActionModule.ServerCycleAction) para chamber la siguiente shell
                var cycleMi = actionModule.GetType().GetMethod("ServerCycleAction",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                cycleMi?.Invoke(actionModule, null);

                // Resync del action module si es AutomaticActionModule (no debería
                // serlo para escopetas, pero por si acaso)
                if (actionModule is AutomaticActionModule aam) aam.ServerResync();
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] _FillShotgun: {ex.Message}");
            }
        }

        // ── Usar item equipado ─────────────────────────────────────────────
        // - Arma de fuego → disparar (Shoot->Click via DummyAction)
        // - Medicamento   → curar (Usable.Use)
        // - Granada       → lanzar (Player.ThrowGrenade)
        // - Keycard       → no hacer nada (se usa al interaccionar con puerta)
        public void UsarItemEquipado(Player player)
        {
            _UsarItemEquipado(player);
        }

        private void _UsarItemEquipado(Player player)
        {
            try
            {
                var cur = player.CurrentItem;
                if (cur == null) return;

                if (cur.Type.ToString().IndexOf("Keycard", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;

                if (cur is Usable usable)
                {
                    usable.Use();
                    return;
                }

                if (cur is Throwable)
                {
                    ProjectileType tipo = cur.Type.GetProjectileType();
                    if (tipo != ProjectileType.None)
                    {
                        player.ThrowGrenade(tipo);
                        // El ThrowGrenade de Exiled NO consume el item de la mano
                        // (crea un throwable nuevo y destruye ESE). Para bots con
                        // FakeConnection hay que quitar manualmente el item original.
                        try { player.RemoveHeldItem(); } catch { }
                    }
                    return;
                }

                if (cur is Firearm)
                {
                    _DispararArma(player, cur);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] UsarItemEquipado: {ex.Message}");
            }
        }

        // ── Disparo directo via ServerShoot (sin dummy actions) ───────────
        // Las dummy actions "Shoot->Click" no existen para bots con FakeConnection.
        // Cada tipo de action module tiene su propio método de disparo:
        //   - AutomaticActionModule:  ServerShoot(ReferenceHub)   [private]
        //   - PumpActionModule:        ShootOneBarrel(bool)         [private]
        //   - DoubleActionModule:      ShootOneBarrel(bool)         [private]
        //   - DisruptorActionModule:   ServerFire(ReferenceHub)    [private]
        // Los llamamos por reflexión. Para revólver/escopeta hay que pre-chamberar
        // la bala manualmente (AmmoStored=1, OpenBolt=false) porque su mecanismo
        // de cycling depende del cliente.
        private float _nextReloadAttempt = 0f;
        private float _nextShotTime = 0f;
        private const float SHOT_COOLDOWN = 0.18f;
        private readonly List<DummyAction> _dummyActions = new List<DummyAction>(16);

        private void _DispararArma(Player player, Item armaEquipada)
        {
            try
            {
                if (!(armaEquipada.Base is InventorySystem.Items.Firearms.Firearm baseFirearm)) return;

                // Cooldown entre disparos
                if (Time.time < _nextShotTime) return;

                // No disparar si ya está recargando
                if (baseFirearm.TryGetModule<IReloaderModule>(out var reloader) && reloader.IsReloadingOrUnloading)
                    return;

                // Comprobar munición: cargador + recámara. Si ambos a 0, recargar.
                baseFirearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo);
                int ammoCargador = primaryAmmo?.AmmoStored ?? 0;

                // Detectar el action module concreto para saber cómo pre-chamberar
                AutomaticActionModule automatic = null;
                PumpActionModule pump = null;
                DoubleActionModule doubleAction = null;
                baseFirearm.TryGetModule(out automatic);
                baseFirearm.TryGetModule(out pump);
                baseFirearm.TryGetModule(out doubleAction);

                int ammoRecamara = 0;
                if (automatic != null) ammoRecamara = automatic.AmmoStored;
                else if (pump != null) ammoRecamara = pump.AmmoStored;
                // DoubleActionModule dispara ambos cañones, no tiene AmmoStored
                else if (doubleAction != null) ammoRecamara = 1;

                if (ammoCargador == 0 && ammoRecamara == 0)
                {
                    _RecargarArma(player);
                    return;
                }

                // Prepara la acción: amartilla, mete bala en recámara si hace falta
                _PrepareActionForShot(baseFirearm);

                // Dispara según el action module
                bool shotFired = false;
                if (automatic != null)
                {
                    automatic.Cocked = true;
                    automatic.BoltLocked = false;
                    // Para revólver: ServerShoot consume de AmmoStored (la cámara).
                    // Si AmmoStored==0 y OpenBolt==true, consume del primary (cilindro).
                    shotFired = _InvokeServerShoot(automatic, player.ReferenceHub, "ServerShoot");
                }
                else if (pump != null)
                {
                    // Escopeta de bombeo: usar ShootOneBarrel(bool) — método privado
                    // que dispara un cartucho y reproduce el sonido.
                    shotFired = _InvokeShotOneBarrel(pump, player.ReferenceHub);
                }
                else if (doubleAction != null)
                {
                    // Escopeta de doble cañón: mismo método ShootOneBarrel
                    shotFired = _InvokeShotOneBarrel(doubleAction, player.ReferenceHub);
                }

                if (shotFired)
                {
                    _nextShotTime = Time.time + SHOT_COOLDOWN;
                }
            }
            catch (System.Exception ex)
            {
                Log.Debug($"[HumanMovement] _DispararArma: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Llama a ServerShoot(ReferenceHub) sobre un action module. Si no existe,
        // fallback a ServerCycleAction(). Devuelve true si disparó.
        private static bool _InvokeServerShoot(object actionModule, ReferenceHub target, string methodName)
        {
            try
            {
                if (actionModule == null) return false;
                var shootMi = actionModule.GetType().GetMethod(methodName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic,
                    null,
                    new[] { typeof(ReferenceHub) },
                    null);
                if (shootMi != null)
                {
                    shootMi.Invoke(actionModule, new object[] { target });
                    return true;
                }
                // Fallback: ServerCycleAction() (no dispara pero cicla)
                var cycleMi = actionModule.GetType().GetMethod("ServerCycleAction",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic,
                    null,
                    System.Type.EmptyTypes,
                    null);
                cycleMi?.Invoke(actionModule, null);
                return false;
            }
            catch (System.Exception ex)
            {
                Log.Debug($"[HumanMovement] _InvokeServerShoot: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        // Llama a ShootOneBarrel(bool) en PumpActionModule / DoubleActionModule
        // para disparar un cartucho de escopeta. Pre-chambera primero.
        private static bool _InvokeShotOneBarrel(object shotgunAction, ReferenceHub target)
        {
            try
            {
                if (shotgunAction == null) return false;

                // 1) Cerrar el bolt (si está abierto) y chamberar una shell
                var t = shotgunAction.GetType();
                var openBoltProp = t.GetProperty("OpenBolt",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                openBoltProp?.SetValue(shotgunAction, false);

                var ammoStoredProp = t.GetProperty("AmmoStored",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                ammoStoredProp?.SetValue(shotgunAction, 1);

                // 2) Llamar a ShootOneBarrel(true) — dispara un cartucho
                var shootMi = t.GetMethod("ShootOneBarrel",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic,
                    null,
                    new[] { typeof(bool) },
                    null);
                if (shootMi == null) return false;

                shootMi.Invoke(shotgunAction, new object[] { true });
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Debug($"[HumanMovement] _InvokeShotOneBarrel: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private void _PrepareActionForShot(InventorySystem.Items.Firearms.Firearm baseFirearm)
        {
            try
            {
                // Detectar todos los action modules y pre-chamberar según corresponda
                baseFirearm.TryGetModule<AutomaticActionModule>(out var automatic);
                baseFirearm.TryGetModule<PumpActionModule>(out var pump);
                baseFirearm.TryGetModule<DoubleActionModule>(out var doubleAction);
                baseFirearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo);

                // Caso 1: AutomaticActionModule (rifles, pistolas, revólver)
                if (automatic != null)
                {
                    automatic.Cocked = true;
                    if (!automatic.OpenBolt
                        && automatic.AmmoStored <= 0
                        && primaryAmmo != null
                        && primaryAmmo.AmmoStored > 0)
                    {
                        _InvokeServerCycleAction(automatic);
                    }
                    automatic.BoltLocked = false;
                    automatic.ServerResync();
                    return;
                }

                // Caso 2: PumpActionModule (escopeta de bombeo) — asegurar cámara=1
                if (pump != null)
                {
                    var t = pump.GetType();
                    var ammoProp = t.GetProperty("AmmoStored",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (ammoProp != null && (int)ammoProp.GetValue(pump) == 0
                        && primaryAmmo != null && primaryAmmo.AmmoStored > 0)
                    {
                        // Cerrar bolt y chamber una shell del tubo
                        var openBoltProp = t.GetProperty("OpenBolt",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic);
                        openBoltProp?.SetValue(pump, false);
                        ammoProp.SetValue(pump, 1);
                        primaryAmmo.ServerModifyAmmo(-1);  // sacar 1 shell del tubo
                    }
                    return;
                }

                // Caso 3: DoubleActionModule (escopeta doble) — siempre lista
                if (doubleAction != null)
                {
                    var t = doubleAction.GetType();
                    var openBoltProp = t.GetProperty("OpenBolt",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    openBoltProp?.SetValue(doubleAction, false);
                    return;
                }
            }
            catch { /* no-op */ }
        }

        // Helper para _PrepareActionForShot — cicla el cerrojo
        private static void _InvokeServerCycleAction(object actionModule)
        {
            try
            {
                if (actionModule == null) return;
                var mi = actionModule.GetType().GetMethod("ServerCycleAction",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic,
                    null,
                    System.Type.EmptyTypes,
                    null);
                mi?.Invoke(actionModule, null);
            }
            catch { /* ignore */ }
        }

        // Dispara un dummy action por nombre (p.ej. "Shoot->Click", "Reload->Click").
        // Populamos la lista con los actions del player + los del inventario
        // (los firearms exponen "Shoot" y "Reload" aquí) y luego invocamos.
        // Devuelve true si encontró y ejecutó el action.
        private bool _ClickDummyAction(Player player, string actionName)
        {
            try
            {
                if (player?.Inventory == null) return false;

                _dummyActions.Clear();
                _dummyActions.AddRange(DummyActionCollector.ServerGetActions(player.ReferenceHub));
                player.Inventory.PopulateDummyActions(_dummyActions.Add, _ => { });

                DummyAction match = default;
                foreach (var variant in _VariantesNombre(actionName))
                {
                    match = _dummyActions.FirstOrDefault(a =>
                        a.Action != null &&
                        string.Equals(a.Name, variant, StringComparison.OrdinalIgnoreCase));
                    if (match.Action != null) break;
                }

                if (match.Action == null)
                {
                    // Búsqueda laxa: por módulo (parte antes de "->")
                    string modulo = _ModuloDeAccion(actionName);
                    if (!string.IsNullOrEmpty(modulo))
                    {
                        match = _dummyActions
                            .Where(a => a.Action != null
                                     && a.Name.IndexOf(modulo, StringComparison.OrdinalIgnoreCase) >= 0
                                     && a.Name.IndexOf("Destroy", StringComparison.OrdinalIgnoreCase) < 0)
                            .OrderBy(a => _ScoreAccion(a.Name))
                            .FirstOrDefault();
                    }
                }

                if (match.Action != null)
                {
                    match.Action.Invoke();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] _ClickDummyAction({actionName}): {ex.Message}");
            }
            return false;
        }

        private static IEnumerable<string> _VariantesNombre(string name)
        {
            string t = name.Trim();
            yield return t;
            if (t.Contains("->")) yield return t.Replace("->", ".");
            if (t.Contains(".")) yield return t.Replace(".", "->");
        }

        private static string _ModuloDeAccion(string name)
        {
            string t = name.Trim();
            int arrow = t.IndexOf("->", StringComparison.Ordinal);
            int dot = t.IndexOf(".", StringComparison.Ordinal);
            int split = (arrow >= 0 && dot >= 0) ? Math.Min(arrow, dot)
                     : Math.Max(arrow, dot);
            return split >= 0 ? t.Substring(0, split) : t;
        }

        private static int _ScoreAccion(string name)
        {
            if (name.IndexOf("Selected", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (name.IndexOf("Click", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (name.IndexOf("New", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (name.IndexOf("Hold", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            return 4;
        }

        // ───────────────────────────────────────────────────────────────────
        // REFLECTION — FpcMouseLook
        // ───────────────────────────────────────────────────────────────────

    }
}