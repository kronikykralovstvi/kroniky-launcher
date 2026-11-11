-- Server-only. The existing bridge secret never enters client files or logs.
local M={}
function M.new()
 luanet.load_assembly('System.Net.Http')
 local File=luanet.import_type('System.IO.File')
 local path='data/kk-web-bridge.key'
 if not File.Exists(path)then error('Existing web bridge key missing')end
 local key=tostring(File.ReadAllText(path)):match('^%s*(.-)%s*$')
 if #key~=64 or not key:match('^[a-f0-9]+$')then error('Invalid bridge key')end
 local Client=luanet.import_type('System.Net.Http.HttpClient')
 local Content=luanet.import_type('System.Net.Http.StringContent')
 local Encoding=luanet.import_type('System.Text.Encoding')
 local TimeSpan=luanet.import_type('System.TimeSpan')
 local client=Client();client.Timeout=TimeSpan.FromSeconds(12)
 client.DefaultRequestHeaders:Add('Authorization','Bearer '..key)
 local pending,content,nextSend,lastSeason=nil,nil,0,nil
 local self={}
 function self.tick(info)
  if not info then return end
  local now=os.time()
  if pending then
   if not pending.IsCompleted then return end
   local ok,response=pcall(function()return pending.Result end)
   if ok then
    if not response.IsSuccessStatusCode then Log('Kroniky season: web refused calendar update')end
    response:Dispose()
   else Log('Kroniky season: web temporarily unavailable')end
   content:Dispose();pending=nil;content=nil
  end
  if now<nextSend and lastSeason==info.seasonIndex then return end
  nextSend=now+15;lastSeason=info.seasonIndex
  local data={year=info.year,month=info.monthIndex,day=info.monthDay,hour=info.hour,minute=info.minute,observedAt=now}
  content=Content(JsonEncode(data),Encoding.UTF8,'application/json')
  local ok,task=pcall(function()return client:PostAsync('https://kronikykralovstvi.cz/season.php',content)end)
  if ok then pending=task else content:Dispose();content=nil;Log('Kroniky season: publication deferred')end
 end
 function self.close()client:Dispose();if content then content:Dispose();content=nil end end
 return self
end
return M
