using System;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace RKmission
{
    // Narrow, opt-in packet trace adapted from knows-helpers/N3Inspector.
    // It records the bank/shop messages needed to establish a verified
    // transaction contract, without enabling any transaction itself.
    internal sealed class LogisticsProbe : IDisposable
    {
        private readonly Action<string> _say;
        private int _messages;
        public bool Active { get; private set; }

        public LogisticsProbe(Action<string> say) { _say = say; }

        public void Start()
        {
            if (Active) return;
            _messages = 0;
            Active = true;
            Network.N3MessageSent += Sent;
            Network.N3MessageReceived += Received;
            _say("Logistics probe active for up to 60 bank/shop/item-transfer messages. " +
                "Perform one bank or vendor action in game, then use /rkm logistics probe stop.");
        }

        public void Stop()
        {
            if (!Active) return;
            Network.N3MessageSent -= Sent;
            Network.N3MessageReceived -= Received;
            Active = false;
            _say("Logistics probe stopped.");
        }

        private void Sent(object sender, N3Message message) => Observe("sent", message);
        private void Received(object sender, N3Message message) => Observe("received", message);

        private void Observe(string direction, N3Message message)
        {
            string details = null;
            if (message is BankMessage bank)
                details = $"Bank identity={bank.Identity}, slots={bank.BankSlots?.Length ?? 0}";
            else if (message is ShopUpdateMessage shop)
                details = $"ShopUpdate slots={shop.VendingMachineSlots?.Length ?? 0}";
            else if (message is VendingMachineFullUpdateMessage vending)
                details = $"VendingMachine owner={vending.OwnerType}:{vending.OwnerInstance}";
            else if (message is ClientContainerAddItem move)
                details = $"MoveToContainer source={move.Source}, target={move.Target}";
            else if (message is ContainerAddItem added)
                details = $"ContainerAddItem source={added.Source}, target={added.Target}, slot={added.Slot}";
            else if (message is ClientMoveItemToInventory take)
                details = $"MoveToInventory source={take.SourceContainer}, slot={take.Slot}";
            else if (message is TradeMessage trade)
                details = $"Trade action={trade.Action}, params={trade.Param1}/{trade.Param2}/{trade.Param3}/{trade.Param4}";
            if (details == null) return;
            _say($"Logistics probe {direction}: {details}; bank open={Inventory.Bank.IsOpen}, " +
                $"main free slots={Inventory.NumFreeSlots}.");
            if (++_messages >= 60) Stop();
        }

        public void Dispose() => Stop();
    }
}
