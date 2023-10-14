"C:\Program Files (x86)\Windows Kits\10\bin\10.0.17134.0\x64\makecert" -r -pe -n "CN=photonCA" -ss CA -sr CurrentUser -a sha256 -cy authority -sky signature -sv MyCA.pvk MyCA.cer

certutil -user -addstore Root MyCA.cer

"C:\Program Files (x86)\Windows Kits\10\bin\10.0.17134.0\x64\makecert" -pe -n "CN=photonSPC" -a sha256 -cy end ^
         -sky signature ^
         -ic MyCA.cer -iv MyCA.pvk ^
         -sv MySPC.pvk MySPC.cer

"C:\Program Files (x86)\Windows Kits\10\bin\10.0.17134.0\x64\pvk2pfx" -pvk MySPC.pvk -spc MySPC.cer -pfx MySPC.pfx