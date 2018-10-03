﻿using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using LibHac;
using static hactoolnet.Print;

namespace hactoolnet
{
    internal static class ProcessNsp
    {
        public static void Process(Context ctx)
        {
            using (var file = new FileStream(ctx.Options.InFile, FileMode.Open, FileAccess.Read))
            {
                Pfs pfs = new Pfs(file);
                ctx.Logger.LogMessage(pfs.Print());

                if (ctx.Options.OutDir != null)
                {
                    pfs.Extract(ctx.Options.OutDir, ctx.Logger);
                }
            }
        }

        private static string Print(this Pfs pfs)
        {
            const int colLen = 65;

            var sb = new StringBuilder();
            sb.AppendLine();

            sb.AppendLine("PFS0:");

            PrintItem(sb, colLen, "Magic:", pfs.Header.Magic);
            PrintItem(sb, colLen, "Number of files:", pfs.Header.NumFiles);

            sb.AppendLine("Files:");
            foreach (PfsFileEntry file in pfs.Files.OrderBy(x => x.Offset))
            {
                PrintItem(sb, colLen, $"{file.Name}", $"{file.Offset:x12}-{file.Offset + file.Size:x12}");
            }

            return sb.ToString();
        }

        public static void CreateNsp(Context ctx, SwitchFs switchFs)
        {
            ulong id = ctx.Options.TitleId;
            if (id == 0)
            {
                ctx.Logger.LogMessage("Title ID must be specified to save title");
                return;
            }

            if (!switchFs.Titles.TryGetValue(id, out Title title))
            {
                ctx.Logger.LogMessage($"Could not find title {id:X16}");
                return;
            }

            var builder = new Pfs0Builder();

            foreach (Nca nca in title.Ncas)
            {
                builder.AddFile(nca.Filename, nca.GetStream());
            }

            var ticket = new Ticket
            {
                SignatureType = TicketSigType.Rsa2048Sha256,
                Signature = new byte[0x200],
                Issuer = "Root-CA00000003-XS00000020",
                FormatVersion = 2,
                RightsId = title.MainNca.Header.RightsId,
                TitleKeyBlock = title.MainNca.TitleKey,
                CryptoType = title.MainNca.Header.CryptoType2,
                SectHeaderOffset = 0x2C0
            };
            byte[] ticketBytes = ticket.GetBytes();
            builder.AddFile($"{ticket.RightsId.ToHexString()}.tik", new MemoryStream(ticketBytes));

            Assembly thisAssembly = Assembly.GetExecutingAssembly();
            Stream cert = thisAssembly.GetManifestResourceStream("hactoolnet.CA00000003_XS00000020");
            builder.AddFile($"{ticket.RightsId.ToHexString()}.cert", cert);


            using (var outStream = new FileStream(ctx.Options.NspOut, FileMode.Create, FileAccess.ReadWrite))
            {
                builder.Build(outStream, ctx.Logger);
            }
        }
    }
}
