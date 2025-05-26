using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Solana.Unity.Metaplex.NFT.Library;
using Solana.Unity.Metaplex.Utilities;
using Solana.Unity.Programs;
using Solana.Unity.Rpc.Builders;
using Solana.Unity.Rpc.Models;
using Solana.Unity.SDK;
using Solana.Unity.Wallet;
using UnityEngine;

public class SingleMintButton : MonoBehaviour
{
    public StoreItemData storeItem; 

    public void BuyItem()
    {
        StartMinting(storeItem).Forget();
    }

    private async UniTask StartMinting(StoreItemData item)
    {
        var mint = new Account();
        var user = Web3.Account;
        var ata = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(user, mint.PublicKey);

        var metadata = new Metadata()
        {
            name = item.itemName,
            symbol = "STKR",
            uri = item.metadataUri,
            sellerFeeBasisPoints = 0,
            creators = new List<Creator> { new(user.PublicKey, 100, true) }
        };

        var blockHash = await Web3.Rpc.GetLatestBlockHashAsync();
        var minRent = await Web3.Rpc.GetMinimumBalanceForRentExemptionAsync(TokenProgram.MintAccountDataSize);

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
                CreateMasterEditionVersion.V3));

        var tx = Transaction.Deserialize(txBuilder.Build(new List<Account> { user, mint }));
        var result = await Web3.Wallet.SignAndSendTransaction(tx);

        if (result?.Result != null)
        {
            await Web3.Rpc.ConfirmTransaction(result.Result);
            Debug.Log($"✅ Minting succeeded!\nExplorer: https://explorer.solana.com/tx/{result.Result}?cluster={Web3.Wallet.RpcCluster.ToString().ToLower()}");
        }
        else
        {
            Debug.LogError("❌ Minting failed.");
        }
    }
}
