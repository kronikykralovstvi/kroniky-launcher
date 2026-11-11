<?php
declare(strict_types=1);
require __DIR__.'/core.php';
header('Cache-Control: no-store, max-age=0');
header('X-Content-Type-Options: nosniff');
try {
 $file=store_dir().'/calendar-state.json';
 if(($_SERVER['REQUEST_METHOD']??'')==='POST'){
  $keyfile=store_dir().'/bridge-key';$key=is_file($keyfile)?trim(file_get_contents($keyfile)):'';
  if(strlen($key)!==64||!hash_equals('Bearer '.$key,$_SERVER['HTTP_AUTHORIZATION']??''))json_out(['error'=>'Unauthorized'],401);
  $raw=file_get_contents('php://input',false,null,0,8193);
  if(strlen($raw)>8192)json_out(['error'=>'Size'],413);
  try{$v=json_decode($raw,true,8,JSON_THROW_ON_ERROR);}catch(Throwable $e){json_out(['error'=>'JSON'],400);}
  if(!is_array($v))json_out(['error'=>'Payload'],400);
  foreach(['year','month','day','hour','minute','observedAt'] as $field)if(!is_int($v[$field]??null))json_out(['error'=>'Field'],400);
  $m=$v['month'];$lengths=[31,28,31,30,31,30,31,31,30,31,30,31];
  if($v['year']<1403||$v['year']>9999||$m<1||$m>12||$v['day']<1||$v['day']>$lengths[$m-1]||$v['hour']<0||$v['hour']>23||$v['minute']<0||$v['minute']>59)json_out(['error'=>'Calendar'],400);
  if(abs(time()-$v['observedAt'])>60)json_out(['error'=>'Clock'],409);
  $season=($m===12||$m<=2)?'winter':($m<=5?'spring':($m<=8?'summer':'autumn'));
  // Derive from the calendar, never from a weather test or client-provided season.
  $clean=['schemaVersion'=>1,'season'=>$season,'year'=>$v['year'],'month'=>$m,'day'=>$v['day'],'hour'=>$v['hour'],'minute'=>$v['minute'],'observedAt'=>$v['observedAt'],'receivedAt'=>time()];
  $lock=fopen($file.'.lock','c');if(!$lock||!flock($lock,LOCK_EX))throw new RuntimeException();
  try {
   $old=is_file($file)?json_decode(file_get_contents($file),true):null;
   if(is_array($old)&&($old['observedAt']??0)>$clean['observedAt'])json_out(['error'=>'Older state'],409);
   if(file_put_contents($file.'.tmp',json_encode($clean,JSON_THROW_ON_ERROR),LOCK_EX)===false||!rename($file.'.tmp',$file))throw new RuntimeException();
  }finally{flock($lock,LOCK_UN);fclose($lock);}
  json_out(['ok'=>true]);
 }
 if(($_SERVER['REQUEST_METHOD']??'')!=='GET')json_out(['error'=>'Method'],405);
 $v=is_file($file)?json_decode(file_get_contents($file),true,8,JSON_THROW_ON_ERROR):null;
 if(!is_array($v)||time()-($v['observedAt']??0)>120||time()-($v['receivedAt']??0)>120)json_out(['error'=>'Server season unavailable'],503);
 json_out($v);
}catch(Throwable $e){json_out(['error'=>'Season service unavailable'],503);}
