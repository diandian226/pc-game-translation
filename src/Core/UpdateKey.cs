namespace LocaForge.Core
{
    /// <summary>
    /// 更新清单签名公钥（RSA 3072）。对应私钥只在发布者本机 signing\update-signing.private.xml，
    /// 由 release.ps1 用来给 update.json 签名。更换密钥后，旧版工具将不再接受新的更新。
    /// </summary>
    static class UpdateKey
    {
        public const string PublicXml =
            "<RSAKeyValue><Modulus>yoZgeTwld2K5eGDwtZKeShP8xZkBdIcVFUbnJNllqOUMAmhaVVpDZRryqGwNDtucp7fRpeTpI6tey7dQsaMxqGVzN7kNHXEbyEnk8934AV//aiDzQdpIUbKwbtvkWImARJI52khMeDKlXjWJtMIO75WOQW6EbqIQKwWKTZHf5KLXFg5vIn93/i3HOEjGIbhAG9/ugL+LNdXHvV+59ijqkR0SlE5Pgr1eAhM8DnwjQP/Atg5isimyUFCl//Sb7wY/BSdvC6AtN84izknlScAMI31Gd87VcpEbAiU2bJDW3q4SoLH4uD6VwJbTbmsch8+IKL2Kbg8R+Nyrpr8/ABagXoAt7kp9dMcdjZZ1IWbr6k0zI1MBeSGUznqrL/tCguInW7moKSIcCdo8+4gKiT0kTmkVYOXkGe6Je5b02NRcOHc1h7k9p3XoK8dqZWw8JQIvnJKOHsNSR66nVKbrcbL+Usw29HtdQ8Q0keE10dWLc7r8d+uyRKkL3B7q5lTAkp5B</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    }
}