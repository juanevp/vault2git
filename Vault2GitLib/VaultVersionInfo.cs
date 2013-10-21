using System;

namespace Vault2Git.Lib
{
    internal struct VaultVersionInfo
    {
        public string Comment { get; set; }
        public string Login { get; set; }
        public DateTime TimeStamp { get; set; }
        public long TrxId { get; set; }
    }
}