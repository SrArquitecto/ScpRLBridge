using Exiled.API.Features;
using PlayerRoles;
using UnityEngine;

namespace ScpAgent.Bot.Strategies.Interfaces
{
    public interface IAgentRoleBaseStrategy
    {
        RoleTypeId Role { get; }
        void InicializarMovimiento(GameObject go, CharacterController cc);

        void MoverPersonaje(int accion, float deltaTime, Player player, GameObject go);
        void MoverCamara(int accion);
        void AbrirPuerta(Player player);

        void OnBind(AgentContext ctx);
        void OnUnbind();
    
        public void OnRoomChanged(Room anterior, Room nueva);
        void OnDamageTaken(float amount, string type);
    }

    public interface IAgentRoleHumanStrategy: IAgentRoleBaseStrategy
    {
        float CalcularPrioridadItem(ItemType tipo);

        // Inventario
        void EquiparTarjeta(Player player);
        void EquiparArmaPrincipal(Player player);
        void EquiparArmaSecundaria(Player player);
        void EquiparMedicamento(Player player);
        void EquiparGranada(Player player);
        void TirarItem(Player player);
        void RecargarArma(Player player);
        void UsarItemEquipado(Player player);
    }
}