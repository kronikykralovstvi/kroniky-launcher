-- Persistent date follows the authoritative world clock; midnight is 00:01.
local M = {}
local dbRef, epoch
local clock,lastTick,lastSave
local Clock=dofile("gamemodes/kk_calendar_clock.lua")
local visiblePlayers, hud = {}, {}
local webPlayers = {}
local YEAR_SECONDS = 30 * 24 * 60 * 60
local SEASONS = {"jaro", "léto", "podzim", "zima"}
local lastWeatherSeason
local winterTestUntil
local publisher,lastTextureWinter,announcedDay
local warnedPlayers={}
local function textureSeason(info)
 local winter=info.seasonIndex==4
 if type(SetGlobalState)=="function" then SetGlobalState("kk.season.actual",({"spring","summer","autumn","winter"})[info.seasonIndex]) end
 if lastTextureWinter~=nil and lastTextureWinter~=winter then
  for pid in pairs(visiblePlayers)do
   if IsPlayerConnected(pid)then SendClientMessage(pid,0xE8D7B0FF,winter and "[Zima] Začala zima. Ukonči hru a spusť ji přes launcher Kronik, aby se načetla zasněžená krajina." or "[Jaro] Zima skončila. Ukonči hru a spusť ji přes launcher Kronik, aby se odstranily zimní textury.")end
  end
 end
 lastTextureWinter=winter
 if info.hour==23 and info.minute>=50 and ((info.monthIndex==11 and info.monthDay==30)or(info.monthIndex==2 and info.monthDay==28))then
  if announcedDay~=info.day then announcedDay=info.day;warnedPlayers={}end
  for pid in pairs(visiblePlayers)do if not warnedPlayers[pid]and IsPlayerConnected(pid)then warnedPlayers[pid]=true;SendClientMessage(pid,0xE8D7B0FF,"[Sezóna] Po herní půlnoci se změní zimní krajina. Potom úplně ukonči hru a znovu ji spusť přes launcher Kronik.")end end
 end
 if publisher then local ok=pcall(publisher.tick,info);if not ok then Log("Kroniky season: publication failed; game continues")end end
end

-- Weather is selected by the server from this calendar, never by a player's save.
local seasonNames = {"spring", "summer", "autumn", "winter"}
local seasonWeather = {
 {clear=15,fair=20,cloudy=15,showers=30,drizzle=18,storm=2},
 {clear=50,fair=30,cloudy=10,showers=7,storm=3},
 {clear=5,fair=10,cloudy=25,overcast=30,drizzle=25,showers=5},
 {clear=20,cloudy=25,overcast=55}
}
function M.applyWeather(info)
 info=info or M.info()
 if not info or not seasonNames[info.seasonIndex] then return false end
 if winterTestUntil and os.time()>=winterTestUntil then winterTestUntil=nil end
 local season=winterTestUntil and 4 or info.seasonIndex
 if type(SetGlobalState)=="function" then
  SetGlobalState("kk.season",seasonNames[season])
  SetGlobalState("kk.season.test",winterTestUntil and "true" or "false")
  if type(GetWeather)=="function" then SetGlobalState("kk.season.weather",GetWeather()) end
 end
 if season==lastWeatherSeason then return false end
 if type(SetWeatherChances)~="function" or type(SetWeather)~="function" then
  Log("Kroniky calendar: seasonal weather requires SetWeatherChances")
  return false
 end
 local ok,result=pcall(SetWeatherChances,seasonWeather[season],3,5)
 if not ok or result==false then return false end
 ok,result=pcall(SetWeather,"",30)
 if not ok or result==false then return false end
 if type(SetRain)=="function" then SetRain(-1) end
 lastWeatherSeason=season
 Log("Kroniky calendar: seasonal weather rotation "..seasonNames[season])
 return true
end

function M.init(db)
  dbRef = db
  if not dbRef then Log("Kroniky calendar: database unavailable"); return false end
  local _, _, err = dbRef:ExecuteSync([[CREATE TABLE IF NOT EXISTS rp_calendar (id INT NOT NULL PRIMARY KEY, epoch BIGINT NOT NULL, start_year INT NOT NULL)]])
  if err then Log("Kroniky calendar: schema error: " .. tostring(err)); return false end
  local rows, queryErr = dbRef:QuerySync("SELECT epoch,start_year FROM rp_calendar WHERE id=1")
  if queryErr then Log("Kroniky calendar: load error: " .. tostring(queryErr)); return false end
  if rows and rows[1] then
    epoch = tonumber(rows[1].epoch)
    M.startYear = tonumber(rows[1].start_year) or 1403
  else
    epoch, M.startYear = os.time(), 1403
    local _, _, insertErr = dbRef:ExecuteSync("INSERT INTO rp_calendar (id,epoch,start_year) VALUES (1,@e,@y)", {e=epoch,y=M.startYear})
    if insertErr then Log("Kroniky calendar: initial save error: " .. tostring(insertErr)); return false end
  end
  if type(GetWorldTime)~='function' or type(GetTimeRatio)~='function' then Log('Calendar: server world-clock API unavailable');return false end
  local _,_,clockErr=dbRef:ExecuteSync('CREATE TABLE IF NOT EXISTS kk_calendar_clock(id INTEGER PRIMARY KEY,absolute_day INTEGER NOT NULL,world_hour REAL NOT NULL)')
  if clockErr then Log('Calendar clock schema failed: '..tostring(clockErr));return false end
  local stored,loadErr=dbRef:QuerySync('SELECT absolute_day,world_hour FROM kk_calendar_clock WHERE id=1')
  if loadErr then Log('Calendar clock load failed: '..tostring(loadErr));return false end
  if stored and stored[1] then
   clock=Clock.new(tonumber(stored[1].absolute_day),tonumber(stored[1].world_hour))
   if SetWorldTime(clock.hour)==false then Log('Calendar: could not restore world time');return false end
  else
   local day=math.floor(math.max(0,os.time()-epoch)/YEAR_SECONDS*365)
   clock=Clock.new(day,GetWorldTime())
   local _,_,saveErr=dbRef:ExecuteSync('INSERT INTO kk_calendar_clock(id,absolute_day,world_hour) VALUES(1,@d,@h)',{d=clock.day,h=clock.hour})
   if saveErr then clock=nil;Log('Calendar migration failed: '..tostring(saveErr));return false end
  end
  local ok,value=pcall(function()return dofile("gamemodes/kk_season_publisher.lua").new()end)
  if ok then publisher=value else Log("Kroniky season: publisher unavailable; launcher requires current calendar")end
  lastTick=GetServerTime();lastSave=lastTick
  SetTimer(function() M.tick() end,1000,true)
  Log("Kroniky calendar: persistent year " .. tostring(M.startYear) .. " ready")
  if type(CreateHudText) == "function" then
    local ok, brand = pcall(CreateHudText, 0.975, 0.018, "KRONIKY KRÁLOVSTVÍ", 0xB89A5CFF, 0.52, HUD_ALIGN_RIGHT)
    if ok then hud.brand = brand end
    ok, hud.main = pcall(CreateHudText, 0.975, 0.038, "ROK 1403  |  JARO", 0xF0D99BFF, 0.82, HUD_ALIGN_RIGHT)
    if not ok then hud.main = nil end
    ok, hud.day = pcall(CreateHudText, 0.975, 0.059, "DEN 001 / 365", 0xD2C49FFF, 0.60, HUD_ALIGN_RIGHT)
    if not ok then hud.day = nil end
    if not hud.brand or not hud.main or not hud.day then
      Log("Kroniky calendar: native HUD text could not be created; check KCD:MP HUD text support")
      hud = {}
    else
      M.updateHudText()
    end
  else
    Log("Kroniky calendar: CreateHudText is unavailable; date HUD disabled")
  end
  M.applyWeather(M.info())
  textureSeason(M.info())
  SetTimer(function() M.applyWeather(M.info()) end, 60000, true)
  -- Keep the small, non-interactive date plate in sync with the same persistent
  -- calendar used by /rok and /datum.
  SetTimer(function() M.refreshHud() end, 2000, true)
  SetTimer(function() M.refreshHud() end, 1200)
  return true
end

function M.tick()
 if not clock then return end
 local now=GetServerTime()
 local nextClock=Clock.new(clock.day,clock.hour)
 local changed=nextClock:step(GetWorldTime(),math.max(0,now-lastTick)/1000,GetTimeRatio())
 if changed>0 or now-lastSave>=30000 then
  local _,_,err=dbRef:ExecuteSync('UPDATE kk_calendar_clock SET absolute_day=@d,world_hour=@h WHERE id=1',{d=nextClock.day,h=nextClock.hour})
  if err then
   if changed>0 then Log('Calendar midnight save failed; will retry: '..tostring(err));return end
  else lastSave=now end
 end
 clock=nextClock;lastTick=now
 M.applyWeather()
 local info=M.info();if info then textureSeason(info)end
end

function M.info(at)
  if not epoch or not clock then return nil end
  local yearOffset=math.floor(clock.day/365)
  local yearProgress=(clock.day%365)/365
  local seasonIndex
  local day = clock.day%365 + 1
  local monthNames={'ledna','února','března','dubna','května','června','července','srpna','září','října','listopadu','prosince'};local lengths={31,28,31,30,31,30,31,31,30,31,30,31};local month,monthDay=1,day;while month<12 and monthDay>lengths[month]do monthDay=monthDay-lengths[month];month=month+1 end
  seasonIndex = (month==12 or month<=2) and 4 or (month<=5 and 1 or (month<=8 and 2 or 3))
  return {weatherTest=winterTestUntil~=nil,monthIndex=month,absoluteDay=clock.day+((clock.hour-1/60)%24)/24,time=string.format("%02d:%02d",math.floor(clock.hour),math.floor(clock.hour*60+1e-6)%60),hour=math.floor(clock.hour),minute=math.floor(clock.hour*60+1e-6)%60,month=monthNames[month],monthDay=monthDay,year=(M.startYear or 1403)+yearOffset, season=SEASONS[seasonIndex], day=day, seasonIndex=seasonIndex}
end

function M.updateHudText()
  local info = M.info()
  if not info or not hud.brand or not hud.main or not hud.day then return false end
  local season = ({["jaro"]="JARO", ["léto"]="LÉTO", ["podzim"]="PODZIM", ["zima"]="ZIMA"})[info.season] or tostring(info.season or "")
  local okBrand = pcall(SetHudText, hud.brand, "KRONIKY KRÁLOVSTVÍ")
  local okMain = pcall(SetHudText, hud.main, string.format("ROK %d  |  %s", info.year, season))
  local okDay = pcall(SetHudText, hud.day, string.format("%d. %s | %s | DEN %03d / 365",info.monthDay,info.month,info.time,info.day))
  return okBrand and okMain and okDay
end

function M.showHud(pid)
  if not IsPlayerConnected(pid)then return false end
  visiblePlayers[pid]=true
  if IsPlayerWebFrameReady and IsPlayerWebFrameReady(pid,'calendar')then
   local info=M.info();if info then SendPlayerWebMessage(pid,'calendar',{calendar=info});ShowPlayerWebFrame(pid,'calendar');webPlayers[pid]=true;for _,id in pairs(hud)do HideHudText(id,pid)end;return true end
  end
  if not hud.brand then return false end
  visiblePlayers[pid] = true
  M.updateHudText()
  local ok = true
  for _, id in ipairs({hud.brand, hud.main, hud.day}) do
    local shown, result = pcall(ShowHudText, id, pid)
    ok = ok and shown and result ~= false
  end
  return ok
end

function M.hideHud(pid)
  visiblePlayers[pid] = nil
  if webPlayers[pid]then HidePlayerWebFrame(pid,"calendar");webPlayers[pid]=nil end
  for _, id in ipairs({hud.brand, hud.main, hud.day}) do
    if id then pcall(HideHudText, id, pid) end
  end
end

function M.refreshHud()
  if not epoch then return end
  M.updateHudText()
  for pid in pairs(visiblePlayers)do M.showHud(pid)end
end

function M.webReady(pid)if visiblePlayers[pid]then M.showHud(pid)end end

function M.command(pid, cmd, args)
  if cmd=="zimatest"then
    if not IsPlayerAdmin(pid)then SendClientMessage(pid,0xE85D5DFF,"[Kalendář] Tento příkaz je jen pro správce.");return true end
    local value=tostring(args or ""):match("^%s*(.-)%s*$")
    if value=="stop"then winterTestUntil=nil
    else
      local minutes=tonumber(value==""and "5"or value)
      if not minutes or minutes%1~=0 or minutes<1 or minutes>15 then SendClientMessage(pid,0xE8D7B0FF,"[Kalendář] /zimatest [1–15 minut|stop]");return true end
      winterTestUntil=os.time()+minutes*60
    end
    lastWeatherSeason=nil;M.applyWeather()
    SendClientMessage(pid,0xE8D7B0FF,winterTestUntil and "[Kalendář] Dočasný test zimního počasí zapnut. Datum a růst plodin zůstávají podle kalendáře. /zimatest stop test ukončí."or "[Kalendář] Test ukončen; počasí opět řídí kalendář.")
    return true
  end
  if cmd ~= "cas" and cmd ~= "čas" and cmd ~= "rok" and cmd ~= "datum" and cmd ~= "sezona" and cmd ~= "sezóna" then return false end
  local info = M.info()
  if not info then
    SendClientMessage(pid, 0xE85D5DFF, "[Kalendář] Databáze kalendáře není dostupná.")
  else
    SendClientMessage(pid, 0xE8D7B0FF, string.format("[Kalendář] %d. %s %d, %s, %s. Datum se posouvá v 00:01 podle hodin serveru.",info.monthDay,info.month,info.year,info.time,info.season))
  end
  return true
end
return M
