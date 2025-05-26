using Solana.Unity.Wallet;
using Solana.Unity.SDK;
using Solana.Unity.Rpc;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.Programs;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

public class SolanaTransfer : MonoBehaviour
{
    [Header("Solana Config")]
    [SerializeField] private Cluster solanaCluster = Cluster.DevNet;
    [SerializeField] private string recipientPublicKey;

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Events")]
    public UnityEvent OnTransactionStart;
    public UnityEvent OnTransactionSigned;
    public UnityEvent OnTransactionSent;
    public UnityEvent OnTransactionConfirmed;
    public UnityEvent OnTransactionFailed;
    public UnityEvent OnTransactionTimeout;

    private IRpcClient rpcClient;
    private bool isTransactionInProgress = false;
    private string lastTransactionSignature = null;

    private void Start()
    {
        rpcClient = ClientFactory.GetClient(solanaCluster);
    }

    public async void OnSendButtonClicked(float amountToSend)
    {
        if (isTransactionInProgress)
        {
            Debug.Log("Transaction already in progress.");
            UpdateStatus("Transaction already in progress...");
            return;
        }

        if (Web3.Account == null)
        {
            Debug.LogError("Wallet not connected!");
            UpdateStatus("Wallet not connected!");
            OnTransactionFailed?.Invoke();
            return;
        }

        isTransactionInProgress = true;
        UpdateStatus("Preparing transaction...");
        OnTransactionStart?.Invoke();

        ulong lamports = (ulong)(amountToSend * 1_000_000_000);

        try
        {
            bool isBrave = Web3.Instance.WalletBase.GetType().Name.Contains("Phantom") == false;

            if (isBrave)
            {
                var result = await Web3.Instance.WalletBase.Transfer(new PublicKey(recipientPublicKey), lamports);

                if (result.WasSuccessful && !string.IsNullOrEmpty(result.Result))
                {
                    string signature = result.Result;
                    lastTransactionSignature = signature;

                    Debug.Log($"✅ Transaction Sent via Brave! Signature: {signature}");
                    OnTransactionSent?.Invoke();

                    UpdateStatus($"✅ Transaction sent! Sig: {signature.Substring(0, 8)}...");
                    OnTransactionConfirmed?.Invoke();
                }
                else
                {
                    Debug.LogWarning($"Brave transfer failed: {result.Reason}");
                    UpdateStatus("Transfer failed.");
                    OnTransactionFailed?.Invoke();
                }
            }
            else
            {
                await SendTransactionManually(lamports);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Exception during transfer: {e.Message}");
            await SendTransactionManually(lamports);
        }
        finally
        {
            isTransactionInProgress = false;
        }
    }

    private async Task SendTransactionManually(ulong lamports)
    {
        try
        {
            var fromPublicKey = Web3.Account.PublicKey;
            var toPublicKey = new PublicKey(recipientPublicKey);

            UpdateStatus("Fetching latest blockhash...");
            var blockHashResult = await rpcClient.GetLatestBlockHashAsync();

            if (!blockHashResult.WasSuccessful)
            {
                Debug.LogError($"Failed to fetch blockhash: {blockHashResult.Reason}");
                UpdateStatus($"Blockhash Error: {blockHashResult.Reason}");
                OnTransactionFailed?.Invoke();
                return;
            }

            UpdateStatus("Building transaction...");
            var transaction = new Transaction
            {
                FeePayer = fromPublicKey,
                RecentBlockHash = blockHashResult.Result.Value.Blockhash
            };
            transaction.Add(SystemProgram.Transfer(fromPublicKey, toPublicKey, lamports));

            UpdateStatus("Please sign the transaction...");

            try
            {
                transaction = await Web3.Instance.WalletBase.SignTransaction(transaction);
            }
            catch (Exception e)
            {
                Debug.LogError($"Signing error: {e.Message}");
                UpdateStatus("Signing failed or cancelled.");
                OnTransactionFailed?.Invoke();
                return;
            }

            if (transaction == null)
            {
                Debug.LogWarning("🚫 Transaction signing cancelled by user.");
                UpdateStatus("Signing cancelled.");
                OnTransactionFailed?.Invoke();
                return;
            }

            OnTransactionSigned?.Invoke();

            UpdateStatus("Sending transaction to blockchain...");

            byte[] signedTx = transaction.Serialize();

            var sendTxResult = await rpcClient.SendTransactionAsync(
                signedTx,
                commitment: Commitment.Confirmed,
                skipPreflight: false
            );

            if (sendTxResult.WasSuccessful)
            {
                string signature = sendTxResult.Result;
                lastTransactionSignature = signature;

                Debug.Log($"✅ Transaction Sent! Signature: {signature}");
                OnTransactionSent?.Invoke();

                UpdateStatus("Confirming transaction...");
                bool confirmed = await ConfirmTransaction(signature);

                if (confirmed)
                {
                    UpdateStatus($"✅ Transaction Completed! Sig: {signature.Substring(0, 8)}...");
                    OnTransactionConfirmed?.Invoke();
                }
                else
                {
                    UpdateStatus("⚠️ Sent, but confirmation timeout or failed.");
                    OnTransactionTimeout?.Invoke();
                }
            }
            else
            {
                Debug.LogError($"❌ Transaction sending failed: {sendTxResult.Reason}");
                UpdateStatus($"Error sending transaction: {sendTxResult.Reason}");
                OnTransactionFailed?.Invoke();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Exception during manual transaction: {e.Message}");
            UpdateStatus($"Error: {e.Message}");
            OnTransactionFailed?.Invoke();
        }
    }

    private async Task<bool> ConfirmTransaction(string signature)
    {
        int retries = 10;
        int delayMs = 1000;
        int attempt = 0;

        while (attempt < retries)
        {
            try
            {
                var statusResult = await rpcClient.GetSignatureStatusesAsync(new List<string> { signature });

                if (statusResult.WasSuccessful && statusResult.Result.Value != null)
                {
                    var statusInfo = statusResult.Result.Value[0];
                    if (statusInfo != null)
                    {
                        if (statusInfo.Signature != null)
                        {
                            Debug.LogError($"❌ Transaction failed: {statusInfo.Signature}");
                            return false;
                        }

                        if (statusInfo.ConfirmationStatus == "confirmed" || statusInfo.ConfirmationStatus == "finalized")
                        {
                            Debug.Log("✅ Transaction fully confirmed!");
                            return true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error while checking confirmation: {e.Message}");
            }

            attempt++;
            await Task.Delay(delayMs);
        }

        Debug.LogWarning("⚠️ Transaction confirmation timed out.");
        return false;
    }

    // 🔵 BUTTON: Refresh transaction status manually
    public async void OnRefreshStatus()
    {
        if (string.IsNullOrEmpty(lastTransactionSignature))
        {
            UpdateStatus("No transaction to refresh.");
            return;
        }

        UpdateStatus("Refreshing transaction status...");
        bool confirmed = await ConfirmTransaction(lastTransactionSignature);

        if (confirmed)
        {
            UpdateStatus($"✅ Transaction Confirmed! Sig: {lastTransactionSignature.Substring(0, 8)}...");
            OnTransactionConfirmed?.Invoke();
        }
        else
        {
            UpdateStatus("⚠️ Transaction not yet confirmed.");
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"Status: {message}");
    }
}
