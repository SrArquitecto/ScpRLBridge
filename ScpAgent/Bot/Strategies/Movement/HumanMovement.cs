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
    ///
    /// IMPORTANTE: los bots usan DummyUtils.SpawnDummy, que crea una conexión
    /// Mirror REAL. Esto permite que el sync visual, los dummy actions
    /// (Shoot->Click, Reload->Click) y los action modules funcionen como
    /// en un cliente humano, sin necesidad de reflexión sobre métodos privados.
    /// </summary>
    public class HumanMovement : BaseMovement
    {
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

        // ───────────────────────────────────────────────────────────────────
        // EJECUCIÓN POR TICK
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

            player.Position = go.transform.position;
        }

        // ───────────────────────────────────────────────────────────────────
        // CATEGORÍAS DE ARMAS
        // ───────────────────────────────────────────────────────────────────
        // Se excluyen GunShotgun y GunRevolver por decisión del proyecto:
        // su flujo de recarga depende de un protocolo cliente-servidor
        // que el bot no puede completar de forma fiable.
        private static readonly HashSet<ItemType> _armasPrimarias = new HashSet<ItemType>
        {
            ItemType.GunE11SR,
            ItemType.GunLogicer,
            ItemType.GunAK,
            ItemType.GunFRMG0,
            ItemType.GunA7,
            ItemType.GunShotgun
        };

        private static readonly HashSet<ItemType> _armasSecundarias = new HashSet<ItemType>
        {
            ItemType.GunCOM15,
            ItemType.GunCOM18,
            ItemType.GunCom45,
            ItemType.GunFSP9,
            ItemType.GunCrossvec,
            ItemType.GunRevolver
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

        // ───────────────────────────────────────────────────────────────────
        // EQUIPAR ITEMS
        // ───────────────────────────────────────────────────────────────────
        // Con DummyUtils.SpawnDummy, player.CurrentItem = item ya actualiza
        // el visual correctamente (la SyncVar llega al cliente real a través
        // de la conexión Mirror del dummy). No hace falta tocar CurInstance.

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

        public void EquiparArmaPrincipal(Player player)
        {
            _EquiparPorCategoria(player, t => _armasPrimarias.Contains(t));
        }

        public void EquiparArmaSecundaria(Player player)
        {
            _EquiparPorCategoria(player, t => _armasSecundarias.Contains(t));
        }

        public void EquiparMedicamento(Player player)
        {
            _EquiparPorCategoria(player, t => _medicamentos.Contains(t));
        }

        public void EquiparGranada(Player player)
        {
            _EquiparPorCategoria(player, t => _granadas.Contains(t));
        }

        // ───────────────────────────────────────────────────────────────────
        // TIRAR ITEM EQUIPADO
        // ───────────────────────────────────────────────────────────────────
        // Con DummyUtils el bot tiene cliente Mirror real, así que
        // player.DropHeldItem() funciona como en un jugador humano:
        // dropea el item, crea un pickup en el suelo y se quita del inventario.

        public void TirarItem(Player player)
        {
            try { Log.Info("Tirar");} //player.DropHeldItem(); }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] TirarItem: {ex.Message}");
            }
        }

        private void _EquiparPorCategoria(Player player, Func<ItemType, bool> pred)
        {
            if (player == null || player.Items == null) return;

            try
            {
                var item = player.Items.FirstOrDefault(i => i != null && pred(i.Type));
                if (item != null) player.CurrentItem = item;
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] _EquiparPorCategoria: {ex.Message}");
            }
        }

        // ───────────────────────────────────────────────────────────────────
        // RECARGAR
        // ───────────────────────────────────────────────────────────────────
        // DummyUtils.SpawnDummy crea un cliente Mirror real, por lo que
        // el protocolo cliente→servidor para revolver/escopeta funciona.
        // Pero como hemos excluido esas armas del set equipable, basta con
        // la dummy action (que ahora SÍ existe) + fallback al ServerTryReload
        // por reflexión sobre IReloaderModule (la interfaz no lo expone).

        public void RecargarArma(Player player)
        {
            _RecargarArma(player);
        }

        private void _RecargarArma(Player player)
        {
            try
            {
                if (!(player.CurrentItem?.Base is InventorySystem.Items.Firearms.Firearm baseFirearm)) return;

                if (baseFirearm.TryGetModule<IReloaderModule>(out var reloader) && reloader.IsReloadingOrUnloading)
                    return;

                baseFirearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo);
                int maxAmmo = primaryAmmo?.AmmoMax ?? 0;
                int currentAmmo = primaryAmmo?.AmmoStored ?? 0;
                if (currentAmmo >= maxAmmo) return;

                if (Time.time < _nextReloadAttempt) return;
                _nextReloadAttempt = Time.time + 0.45f;

                // ── Caso revólver: bypass del protocolo cliente→servidor ─────────
                // El ServerTryReload abre el cilindro y espera confirmación del cliente.
                // El bot con FakeConnection nunca confirma, así que rellenamos
                // el cilindro directamente + reseteamos IsReloading.
                if (baseFirearm.TryGetModule<RevolverClipReloaderModule>(out var revolver))
                {
                    if (primaryAmmo != null)
                        primaryAmmo.ServerModifyAmmo(maxAmmo - currentAmmo);
                    _ResetIsReloading(revolver);
                    return;
                }

                // ── Caso escopeta (PumpActionModule / DoubleActionModule) ─────────
                // Similar al revólver: rellenar el tubo y chamber una shell.
                if (baseFirearm.TryGetModule<PumpActionModule>(out var pump)
                    || baseFirearm.TryGetModule<DoubleActionModule>(out _))
                {
                    if (primaryAmmo != null && currentAmmo < maxAmmo)
                        primaryAmmo.ServerModifyAmmo(maxAmmo - currentAmmo);
                    // Forzar AmmoStored=1 (chambered) y resetear IsReloading
                    if (pump != null)
                    {
                        var t = pump.GetType();
                        var ammoProp = t.GetProperty("AmmoStored",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        ammoProp?.SetValue(pump, 1);
                    }
                    if (baseFirearm.TryGetModule<IReloaderModule>(out var pumpReloader))
                        _ResetIsReloading(pumpReloader);
                    return;
                }

                // ── Resto de armas (rifles, pistolas, SMGs): protocolo normal ──
                // (1) Dummy action "Reload->Click" — la forma canónica
                bool fired = _ClickDummyAction(player, "Reload->Click");

                // (2) Fallback: IReloaderModule.ServerTryReload
                if (!fired && reloader != null)
                {
                    var mi = reloader.GetType().GetMethod("ServerTryReload",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    mi?.Invoke(reloader, null);
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] RecargarArma: {ex.Message}");
            }
        }

        // Fuerza IsReloading=false para desbloquear recargas futuras cuando
        // el bot ha rellenado ammo por bypass (revólver/escopeta).
        private static void _ResetIsReloading(IReloaderModule reloader)
        {
            try
            {
                var prop = typeof(AnimatorReloaderModuleBase).GetProperty("IsReloading",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                prop?.SetValue(reloader, false);
            }
            catch { /* ignore */ }
        }

        // ───────────────────────────────────────────────────────────────────
        // USAR ITEM
        // ───────────────────────────────────────────────────────────────────
        // - Arma → disparar (dummy action + IHitregModule.Fire fallback)
        // - Medicamento → Usable.Use()
        // - Granada → player.ThrowGrenade (consume el item correctamente)
        // - Keycard → no hacer nada (se activa al interaccionar con puerta)

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
                        // ThrowGrenade de Exiled crea un throwable nuevo y destruye ESE,
                        // no el item que tiene el bot en la mano. Hay que quitarlo manualmente.
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

        // ───────────────────────────────────────────────────────────────────
        // DISPARAR
        // ───────────────────────────────────────────────────────────────────
        // La dummy action "Shoot->Click" ahora SÍ existe (el bot tiene cliente
        // Mirror real). Como fallback usamos IHitregModule.Fire que registra
        // el hit si la dummy action no está disponible para este tipo de arma.

        private float _nextReloadAttempt = 0f;
        private float _nextShotTime = 0f;
        private const float SHOT_COOLDOWN = 0.18f;
        private readonly List<DummyAction> _dummyActions = new List<DummyAction>(16);

        private void _DispararArma(Player player, Item armaEquipada)
        {
            try
            {
                if (!(armaEquipada.Base is InventorySystem.Items.Firearms.Firearm baseFirearm)) return;
                if (Time.time < _nextShotTime) return;

                if (baseFirearm.TryGetModule<IReloaderModule>(out var reloader) && reloader.IsReloadingOrUnloading)
                    return;

                baseFirearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo);
                int ammoCargador = primaryAmmo?.AmmoStored ?? 0;
                int ammoRecamara = _GetChamberAmmo(baseFirearm);

                if (ammoCargador == 0 && ammoRecamara == 0)
                {
                    _RecargarArma(player);
                    return;
                }

                // Prepara: amartilla + chamber si hace falta
                _PrepareChamber(baseFirearm);

                // Caso revólver: ServerShoot consume de AmmoStored si la cámara tiene
                // bala y OpenBolt=false. Si no, consume del primary (cilindro).
                // Con dummy action "Shoot->Click" esto debería funcionar,
                // pero pre-establecemos AmmoStored=1 y OpenBolt=false para asegurar.
                if (baseFirearm.TryGetModule<RevolverClipReloaderModule>(out var revolver))
                {
                    _PrepareRevolverShot(revolver, baseFirearm, primaryAmmo);
                }

                // (1) Dummy action "Shoot->Click"
                bool fired = _ClickDummyAction(player, "Shoot->Click");

                // (2) Fallback: IHitregModule.Fire si la dummy action no existe
                if (!fired && baseFirearm.TryGetModule<IHitregModule>(out var hitreg))
                {
                    hitreg.Fire(player.ReferenceHub, new BulletShotEvent(baseFirearm.ItemId, 0));
                }

                _nextShotTime = Time.time + SHOT_COOLDOWN;
            }
            catch (Exception ex)
            {
                Log.Debug($"[HumanMovement] _DispararArma: {ex.Message}");
            }
        }

        // Para el revólver: cerrar el cilindro (OpenBolt=false) y chamber una bala
        // del cilindro. Sin esto, ServerShoot falla porque AmmoStored=0.
        // OpenBolt es read-only en AutomaticActionModule, así que usamos reflexión
        // para setearlo en la instancia concreta.
        private static void _PrepareRevolverShot(RevolverClipReloaderModule revolver,
            InventorySystem.Items.Firearms.Firearm baseFirearm,
            IPrimaryAmmoContainerModule primaryAmmo)
        {
            try
            {
                if (!baseFirearm.TryGetModule<AutomaticActionModule>(out var automatic)) return;
                // Cerrar cilindro via reflexión
                var openBoltProp = typeof(AutomaticActionModule).GetProperty("OpenBolt",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                openBoltProp?.SetValue(automatic, false);
                // Chamber una bala del cilindro
                if (automatic.AmmoStored == 0 && primaryAmmo != null && primaryAmmo.AmmoStored > 0)
                {
                    _InvokeCycleAction(automatic);
                }
            }
            catch { /* ignore */ }
        }

        // Devuelve el número de balas en la recámara del arma (1 si hay round
        // chambered, 0 si no). Para armas de doble cañón, asumimos siempre 1.
        private static int _GetChamberAmmo(InventorySystem.Items.Firearms.Firearm baseFirearm)
        {
            if (baseFirearm.TryGetModule<AutomaticActionModule>(out var automatic))
                return automatic.AmmoStored;
            if (baseFirearm.TryGetModule<PumpActionModule>(out var pump))
                return pump.AmmoStored;
            // DoubleActionModule no tiene AmmoStored — siempre lista
            if (baseFirearm.TryGetModule<DoubleActionModule>(out _))
                return 1;
            return 0;
        }

        // Pre-chambera una bala del cargador a la recámara si está vacía.
        // Solo AutomaticActionModule y PumpActionModule tienen este flujo.
        private static void _PrepareChamber(InventorySystem.Items.Firearms.Firearm baseFirearm)
        {
            try
            {
                if (baseFirearm.TryGetModule<AutomaticActionModule>(out var automatic))
                {
                    automatic.Cocked = true;
                    if (!automatic.OpenBolt
                        && automatic.AmmoStored <= 0
                        && baseFirearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo)
                        && primaryAmmo.AmmoStored > 0)
                    {
                        _InvokeCycleAction(automatic);
                    }
                    automatic.BoltLocked = false;
                    automatic.ServerResync();
                    return;
                }

                if (baseFirearm.TryGetModule<PumpActionModule>(out var pump))
                {
                    var t = pump.GetType();
                    var openBoltProp = t.GetProperty("OpenBolt",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    openBoltProp?.SetValue(pump, false);
                    var ammoProp = t.GetProperty("AmmoStored",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (ammoProp != null && (int)ammoProp.GetValue(pump) == 0)
                    {
                        if (baseFirearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo)
                            && primaryAmmo.AmmoStored > 0)
                        {
                            ammoProp.SetValue(pump, 1);
                            primaryAmmo.ServerModifyAmmo(-1);
                        }
                    }
                    return;
                }
            }
            catch { /* no-op */ }
        }

        // ───────────────────────────────────────────────────────────────────
        // DUMMY ACTIONS
        // ───────────────────────────────────────────────────────────────────
        // Las dummy actions simulan input del cliente. Con DummyUtils.SpawnDummy
        // el bot tiene una conexión Mirror real, así que las acciones SÍ están
        // disponibles (Shoot->Click, Reload->Click, etc.).
        // Busca por nombre (o variante de puntos/guiones) y la invoca.

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
        // REFLECTION — ServerCycleAction
        // ───────────────────────────────────────────────────────────────────
        // IActionModule no expone ServerCycleAction, así que lo localizamos
        // por reflexión en la implementación concreta (AutomaticActionModule
        // o PumpActionModule).
        private static void _InvokeCycleAction(object actionModule)
        {
            try
            {
                if (actionModule == null) return;
                var mi = actionModule.GetType().GetMethod("ServerCycleAction",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                mi?.Invoke(actionModule, null);
            }
            catch { /* ignore */ }
        }

        // ───────────────────────────────────────────────────────────────────
        // REFLECTION — FpcMouseLook
        // ───────────────────────────────────────────────────────────────────
    }
}
