param(
    $CertPath = ".\server.pfx",
    $ClientCertName = "client-test",
    $ClientCertPassword = $null,
    $ServerUrl = "https://localhost:8080",
    $SecurityClearance = "ClusterAdmin",
    [switch]$ServerAdmin = $false,
    [String[]]$DatabaseNames,
    [String[]]$DatabaseAccess
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ServerUrl = $ServerUrl.TrimEnd("/")

$ValidSecClearance = @( 'ClusterAdmin', 'Operator', 'DatabaseAdmin', 'ValidUser' )

if (($ValidSecClearance | Where-Object { $_ -eq $SecurityClearance } | Select-Object -First 1) -eq $null) {
    write-host "Invalid security clearance option. Provide one of the following: $ValidSecClearance"
    exit 1;
}

$permissions = @{}
for ($i = 0; $i -lt $DatabaseNames.Length; $i++) {
    $permissions[$DatabaseNames[$i]] = $DatabaseAccess[$i]
}

$payload = @{
    Name = $ClientCertName;
    SecurityClearance = $SecurityClearance; # ClusterAdmin, Operator
    Password = $ClientCertPassword;
    Permissions = $permissions
} | ConvertTo-Json

$serverCert = Get-PfxCertificate -FilePath $CertPath;
$url = "$ServerUrl/admin/certificates"

write-host "Sending client cert request: $payload"

[Net.ServicePointManager]::ServerCertificateValidationCallback = [System.Net.Security.RemoteCertificateValidationCallback] {
    param($sender, $certificate, $chain, $sslPolicyErrors)

    # validate whether server cert is used for obtaining client cert
    $result = $certificate.Thumbprint -eq $serverCert.Thumbprint
    if ($result -eq $False) {
        throw "Certificate used for obtaining client certificate must be same as server certificate."
    }

    return $result
}

Invoke-WebRequest `
    -Verbose `
    -Debug `
    -Method POST `
    -Certificate $serverCert `
    -Body $payload `
    -ContentType "application/json" `
    -OutFile "$ClientCertName.pfx" `
    $url
