using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Events;
using Robust.Client.UserInterface;

namespace Content.Client._NF.Bank.UI;

public sealed class StationAdminConsoleBoundUserInterface : BoundUserInterface
{
    private StationAdminConsoleMenu? _menu;

    public StationAdminConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) {}

    protected override void Open()
    {
        base.Open();
        if (_menu == null)
        {
            _menu = this.CreateWindow<StationAdminConsoleMenu>();
            _menu.WithdrawRequest += OnWithdraw;
            _menu.DepositRequest += OnDeposit;
            // radiant start
            _menu.CargoTaxApply += OnCargoTaxApply;
            _menu.AtmTaxApply += OnAtmTaxApply;
            _menu.VatTaxApply += OnVatTaxApply;
            _menu.ShuttleSellApply += OnShuttleSellApply;
            // radiant end
            _menu.PopulateReasons();
        }
    }

    private void OnWithdraw()
    {
        if (_menu?.WithdrawalAmount is not int amount)
            return;
        SendMessage(new StationBankWithdrawMessage(amount, _menu.WithdrawalReason, _menu.WithdrawalDescription));
    }

    private void OnDeposit()
    {
        if (_menu?.DepositAmount is not int amount)
            return;
        SendMessage(new StationBankDepositMessage(amount, _menu.DepositReason, _menu.DepositDescription));
    }

    // radiant start
    private void OnCargoTaxApply()
    {
        if (_menu?.CargoTaxText is not { } text)
            return;
        if (float.TryParse(text, out var percent))
        {
            var rate = percent / 100f;
            SendMessage(new StationBankSetTaxMessage(rate, _menu.AtmTaxRate, _menu.VatTaxRate, _menu.ShuttleSellRate));
        }
    }

    private void OnAtmTaxApply()
    {
        if (_menu?.AtmTaxText is not { } text)
            return;
        if (float.TryParse(text, out var percent))
        {
            var rate = percent / 100f;
            SendMessage(new StationBankSetTaxMessage(_menu.CargoTaxRate, rate, _menu.VatTaxRate, _menu.ShuttleSellRate));
        }
    }

    private void OnVatTaxApply()
    {
        if (_menu?.VatTaxText is not { } text)
            return;
        if (float.TryParse(text, out var percent))
        {
            var rate = percent / 100f;
            SendMessage(new StationBankSetTaxMessage(_menu.CargoTaxRate, _menu.AtmTaxRate, rate, _menu.ShuttleSellRate));
        }
    }

    private void OnShuttleSellApply()
    {
        if (_menu?.ShuttleSellText is not { } text)
            return;
        if (float.TryParse(text, out var percent))
        {
            var rate = percent / 100f;
            SendMessage(new StationBankSetTaxMessage(_menu.CargoTaxRate, _menu.AtmTaxRate, _menu.VatTaxRate, rate));
        }
    }
    // radiant end

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not StationBankATMMenuInterfaceState bankState)
            return;
        _menu?.SetEnabled(bankState.Enabled);
        _menu?.SetBalance(bankState.Balance);
        _menu?.SetDeposit(bankState.Deposit);
        // radiant start
        _menu?.SetCargoTaxRate(bankState.CargoTaxRate);
        _menu?.SetAtmDepositTaxRate(bankState.AtmDepositTaxRate);
        _menu?.SetVatTaxRate(bankState.VendorVatRate);
        _menu?.SetShuttleSellRate(bankState.ShuttleSellRate);
        // radiant end
    }
}
