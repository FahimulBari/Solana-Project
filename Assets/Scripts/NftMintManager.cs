using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Solana.Unity.Metaplex.NFT.Library;
using Solana.Unity.Metaplex.Utilities;
using Solana.Unity.Programs;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Models;
using Solana.Unity.Rpc.Types;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NftMintManager : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI progressText;
    public TextMeshProUGUI debugText;
    public Button checkButton;

    [Header("Pinata Gateway Prefix")]
    public string pinataGateway = "https://coral-efficient-koala-902.mypinata.cloud/ipfs/";

    private bool isMinting = false;
    private Coroutine loadingCoroutine;

    /// <summary>
    /// Call this from a UI button or script, passing the item to mint.
    /// </summary>
    public void Mint(StoreItemData item)
    {
        checkButton.interactable = false;

        if (item == null)
        {
            UpdateDebug("❌ Item is null");
            return;
        }

        if (isMinting)
        {
            UpdateDebug("⚠️ Already minting...");
            return;
        }

        StartMinting(item).Forget();
    }

    private async UniTaskVoid StartMinting(StoreItemData item)
    {
        isMinting = true;

        try
        {
            // Start loading animation
            StartLoadingAnimation();

            UpdateProgress("🔧 Preparing mint...");
            UpdateDebug($"Minting: {item.itemName}");

            var mint = new Account();
            var user = Web3.Account;
            var ata = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(user, mint.PublicKey);

            UpdateProgress("📄 Creating metadata...");
            await UniTask.Delay(500); // Small delay for UI feedback

            var metadata = new Metadata()
            {
                name = item.itemName,
                symbol = "STKR",
                uri = $"{pinataGateway}{item.metadataUri}",
                sellerFeeBasisPoints = 0,
                creators = new List<Creator> { new(user.PublicKey, 100, true) }
            };

            UpdateProgress("🌐 Getting blockchain info...");
            var blockHash = await Web3.Rpc.GetLatestBlockHashAsync();
            var minRent = await Web3.Rpc.GetMinimumBalanceForRentExemptionAsync(TokenProgram.MintAccountDataSize);

            UpdateProgress("🔨 Building transaction...");
            await UniTask.Delay(300);

            var txBuilder = new TransactionBuilder()
                .SetRecentBlockHash(blockHash.Result.Value.Blockhash)
                .SetFeePayer(user)
                .AddInstruction(SystemProgram.CreateAccount(user, mint.PublicKey, minRent.Result, TokenProgram.MintAccountDataSize, TokenProgram.ProgramIdKey))
                .AddInstruction(TokenProgram.InitializeMint(mint.PublicKey, 0, user, user))
                .AddInstruction(AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(user, user, mint.PublicKey))
                .AddInstruction(TokenProgram.MintTo(mint.PublicKey, ata, 1, user))
                .AddInstruction(MetadataProgram.CreateMetadataAccount(
                    PDALookup.FindMetadataPDA(mint),
                    mint.PublicKey,
                    user,
                    user,
                    user.PublicKey,
                    metadata,
                    TokenStandard.NonFungible,
                    true,
                    true,
                    null,
                    metadataVersion: MetadataVersion.V3))
                .AddInstruction(MetadataProgram.CreateMasterEdition(
                    null,
                    PDALookup.FindMasterEditionPDA(mint),
                    mint,
                    user,
                    user,
                    user,
                    PDALookup.FindMetadataPDA(mint),
                    CreateMasterEditionVersion.V3
                ));

            UpdateProgress("✍️ Signing transaction...");
            var tx = Transaction.Deserialize(txBuilder.Build(new List<Account> { user, mint }));

            UpdateProgress("📡 Sending to blockchain...");
            var result = await Web3.Wallet.SignAndSendTransaction(tx);

            if (!string.IsNullOrEmpty(result?.Result))
            {
                UpdateProgress("⏳ Waiting for confirmation...");

                TransactionMeta meta = null;
                int retryCount = 0;
                string explorerUrl = "";

                while (retryCount < 10) // Retry up to 10 times (~10 seconds)
                {
                    var confirmation = await Web3.Rpc.GetTransactionAsync(result.Result, Commitment.Confirmed);

                    if (confirmation.WasSuccessful && confirmation.Result?.Transaction != null)
                    {
                        meta = confirmation.Result.Meta;
                        break;
                    }

                    await UniTask.Delay(1000);
                    retryCount++;
                }

                StopLoadingAnimation();

                if (meta != null)
                {
                    UpdateProgress("✅ Mint Complete!");
                    UpdateDebug($"✅ {mint.PublicKey.ToString()[..8]}... Confirmed");

                    Debug.Log("=== MINT SUCCESS ===");
                    Debug.Log($"Item: {item.itemName}");
                    Debug.Log($"Mint Address: {mint.PublicKey}");

                    explorerUrl = $"https://explorer.solana.com/address/{mint.PublicKey}?cluster={Web3.Wallet.RpcCluster.ToString().ToLower()}";
                }
                else
                {
                    UpdateProgress("⚠️ Tx not confirmed (timeout)");
                    UpdateDebug("⚠️ Transaction sent but no confirmation after retries");

                    explorerUrl = $"https://explorer.solana.com/tx/{result.Result}?cluster={Web3.Wallet.RpcCluster.ToString().ToLower()}";
                }

                // Activate the button regardless of success or timeout
                if (checkButton != null)
                {
                    checkButton.interactable = true;
                    checkButton.onClick.RemoveAllListeners(); // Clear old listeners
                    checkButton.onClick.AddListener(()=> OpenExplorerLink(explorerUrl));
                }

            }
            else
            {
                StopLoadingAnimation();
                UpdateProgress("❌ Failed");
                UpdateDebug("❌ Transaction failed (no signature returned)");
                Debug.LogError("Minting transaction failed — no signature returned");
            }
        }
        catch (System.Exception ex)
        {
            StopLoadingAnimation();
            UpdateProgress("❌ Error");
            UpdateDebug($"❌ Error: {ex.Message}");
            Debug.LogError($"Minting error: {ex}");
        }
        finally
        {
            isMinting = false;
        }
    }


    private void StartLoadingAnimation()
    {
        if (loadingCoroutine != null)
            StopCoroutine(loadingCoroutine);
        loadingCoroutine = StartCoroutine(LoadingAnimation());
    }

    private void StopLoadingAnimation()
    {
        if (loadingCoroutine != null)
        {
            StopCoroutine(loadingCoroutine);
            loadingCoroutine = null;
        }
    }

    private IEnumerator LoadingAnimation()
    {
        string[] dots = { "", ".", "..", "..." };
        int index = 0;
        
        while (true)
        {
            if (progressText != null)
            {
                string currentText = progressText.text;
                if (currentText.Contains("..."))
                    currentText = currentText.Replace("...", "");
                if (currentText.Contains(".."))
                    currentText = currentText.Replace("..", "");
                if (currentText.Contains("."))
                    currentText = currentText.Replace(".", "");
                
                progressText.text = currentText + dots[index];
            }
            
            index = (index + 1) % dots.Length;
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void UpdateProgress(string message)
    {
        if (progressText != null)
        {
            progressText.text = message;
        }
        Debug.Log($"Progress: {message}");
    }

    private void UpdateDebug(string message)
    {
        Debug.Log(message);
        if (debugText != null)
        {
            debugText.text = message;
        }
    }

    public void OpenExplorerLink(string explorerUrl)
    {
        if (!string.IsNullOrEmpty(explorerUrl))
        {
            Application.OpenURL(explorerUrl);
            Debug.Log("🌐 Opened: " + explorerUrl);
        }
        else
        {
            Debug.LogWarning("❌ Explorer URL is empty.");
        }
    }
}