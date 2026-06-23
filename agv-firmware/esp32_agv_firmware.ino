/*
 * VisiPick AGV_2 - FINAL
 * JSON status + TRAYS_READY_3 + LEAVE_HOME + GO_START
 * TAG_START = D6:B9:39:F4
 */
#include <SPI.h>
#include <MFRC522.h>
#include <WiFi.h>
#include <PubSubClient.h>
#include <WebServer.h>
#include <ACB_SmartCar_V2.h>
#include <QTRSensors.h>

const char* WIFI_SSID="moble_classroom";
const char* WIFI_PASSWORD="moble2025";
const char* MQTT_SERVER="192.168.0.15";
const int MQTT_PORT=1883;
const char* MQTT_CLIENT_ID="AGV_2";
const char* MQTT_TOPIC_CMD="visipick/agv/2/command";
const char* MQTT_TOPIC_STATUS="visipick/agv/2/status";

WiFiClient wifiClient;
PubSubClient mqtt(wifiClient);
WebServer httpServer(80);

#define RFID_SS 5
#define RFID_RST 17
MFRC522 mfrc(RFID_SS, RFID_RST);

const byte TAG_1[]={0x3B,0x47,0x3D,0x5A};
const byte TAG_2[]={0x3B,0x47,0x42,0x5A};
const byte TAG_START[]={0xD6,0xB9,0x39,0xF4};

unsigned long lastRfidTime=0;
const unsigned long RFID_COOLDOWN=100;
const unsigned long RFID_SAME_IGNORE=2500;
String lastRfidUid="";
unsigned long lastSameUidMs=0;
unsigned long rfidPauseUntil=0;
bool tag2Found=false;
int lastParkedHome=0;
int exitFromHome=0;
bool homeExitMode=false;
bool homeExitTapeArmed=false;
unsigned long homeExitTapeAt=0;

ACB_SmartCar_V2 ACB_SmartCar;
QTRSensors qtr;

const uint8_t SensorCount=8;
uint16_t sensorValues[SensorCount];
int lineValue[SensorCount];
const uint8_t sensorPins[SensorCount]={4,13,14,12,25,26,27,2};
int whiteBase[SensorCount]={568,395,344,395,420,386,405,242};
int weight[SensorCount]={3500,2500,1500,500,-500,-1500,-2500,-3500};

int baseSpeed=200;
const int MIN_MOTOR_SPEED=90;
const int MAX_MOTOR_SPEED=255;
const int LEFT_MOTOR_TRIM=10;
const bool PID_REVERSE=true;
const int MIN_LINE_SUM=30;

int pidLastError=0;
int pidFilteredError=0;
bool hardTurnLock=false;
unsigned long hardTurnIgnoreUntil=0;

#define PIN_ULTRA_TRIG 33
#define PIN_ULTRA_ECHO 34
const int OBSTACLE_STOP_CM=15;
const int OBSTACLE_CLEAR_CM=20;
const int OBSTACLE_CONFIRM_COUNT=2;
const int OBSTACLE_CLEAR_COUNT=2;
unsigned long usLastMeasureMs=0;
int usObsCount=0,usClrCount=0;
bool usObstacleHold=false;

enum AgvState { ST_IDLE, ST_FOLLOW, ST_WH_STOP, ST_UTURN, ST_RETURN, ST_HOME_APPROACH, ST_HOME_EXIT, ST_GOING_START, ST_EMERGENCY };
enum Mission { M_NONE, M_WH1, M_WH2 };

AgvState state=ST_IDLE;
Mission mission=M_NONE;
Mission pendingMission=M_NONE;
bool home1Busy=false,home2Busy=false,home3Busy=false;
int targetHome=0;

unsigned long whStopStart=0;
const unsigned long WH_STOP_MS=5000;
unsigned long lastStatusMs=0;

const int TURN_LEFT_INNER=-150;
const int TURN_LEFT_OUTER=220;
const int TURN_RIGHT_OUTER=220;
const int TURN_RIGHT_INNER=-150;
const unsigned long TURN_TRACK_MS=400;

enum TurnMode { TM_NONE, TM_LEFT, TM_RIGHT };
TurnMode turnMode=TM_NONE;
unsigned long turnStartMs=0;
bool turnForReturn=false;

bool warehouseStopArmed=false;
unsigned long warehouseStopArmedAt=0;
const unsigned long WH_TAPE_IGNORE_MS=800;
const int WH_TAPE_HIT_COUNT=6;
const long WH_TAPE_TOTAL_MIN=1200;

bool homeStopArmed=false;
unsigned long homeStopArmedAt=0;
const unsigned long HOME_TAPE_IGNORE_MS=1200;
const int HOME_TAPE_HIT_COUNT=4;
const long HOME_TAPE_TOTAL_MIN=700;

const int RETURN_BRANCH_THRESHOLD=30;
const unsigned long RETURN_BRANCH_IGNORE_MS=1100;
const unsigned long RETURN_BRANCH_TURN_MS=500;
unsigned long returnTrackStartMs=0;
int returnFromWh=0;

const unsigned long UTURN_MS=1600;
unsigned long uTurnStartMs=0;
unsigned long goingStartAt=0;

const int HOME_ACTION[4]={0,-1,0,1};

void handleTag(int tagId);

// ── JSON status ──
void publishStatus(const char* statusStr){
  Serial.printf("[STATUS] %s\n",statusStr);
  if(!mqtt.connected())return;
  char missionStr[20]="NONE";
  if(pendingMission==M_WH1||mission==M_WH1)strcpy(missionStr,"GO_WAREHOUSE_1");
  else if(pendingMission==M_WH2||mission==M_WH2)strcpy(missionStr,"GO_WAREHOUSE_2");
  char b[300];
  snprintf(b,sizeof(b),
    "{\"agv_id\":\"AGV_2\",\"status\":\"%s\",\"mission\":\"%s\","
    "\"home1_free\":%s,\"home2_free\":%s,\"home3_free\":%s,"
    "\"rssi\":%d}",
    statusStr,missionStr,
    home1Busy?"false":"true",home2Busy?"false":"true",home3Busy?"false":"true",
    WiFi.RSSI());
  mqtt.publish(MQTT_TOPIC_STATUS,b);
}
void publishHeartbeat(){
  if(!mqtt.connected())return;
  const char* st="IDLE";
  if(state==ST_FOLLOW)st="TRACKING";
  else if(state==ST_RETURN||state==ST_HOME_APPROACH)st="RETURNING";
  else if(state==ST_WH_STOP)st="AT_WAREHOUSE";
  else if(state==ST_GOING_START||state==ST_HOME_EXIT)st="GOING_START";
  else if(state==ST_EMERGENCY)st="EMERGENCY";
  else if(pendingMission!=M_NONE)st="STANDBY";
  char missionStr[20]="NONE";
  if(pendingMission==M_WH1||mission==M_WH1)strcpy(missionStr,"GO_WAREHOUSE_1");
  else if(pendingMission==M_WH2||mission==M_WH2)strcpy(missionStr,"GO_WAREHOUSE_2");
  char b[300];
  snprintf(b,sizeof(b),
    "{\"agv_id\":\"AGV_2\",\"status\":\"%s\",\"mission\":\"%s\","
    "\"home1_free\":%s,\"home2_free\":%s,\"home3_free\":%s,"
    "\"rssi\":%d}",
    st,missionStr,
    home1Busy?"false":"true",home2Busy?"false":"true",home3Busy?"false":"true",
    WiFi.RSSI());
  mqtt.publish(MQTT_TOPIC_STATUS,b);
}

// ── 모터 ──
int avoidDeadSpeed(int s){if(s==0)return 0;int a=abs(s);if(a>=102&&a<=104)a=105;return s<0?-a:a;}
void setFourMotor(int lf,int lr,int rf,int rr){
  if(lf!=0)lf+=(lf>0)?LEFT_MOTOR_TRIM:-LEFT_MOTOR_TRIM;
  if(lr!=0)lr+=(lr>0)?LEFT_MOTOR_TRIM:-LEFT_MOTOR_TRIM;
  lf=constrain(lf,-255,255);lr=constrain(lr,-255,255);rf=constrain(rf,-255,255);rr=constrain(rr,-255,255);
  lf=avoidDeadSpeed(lf);lr=avoidDeadSpeed(lr);rf=avoidDeadSpeed(rf);rr=avoidDeadSpeed(rr);
  ACB_SmartCar.motorControl(1,lf);ACB_SmartCar.motorControl(2,lr);ACB_SmartCar.motorControl(3,rf);ACB_SmartCar.motorControl(4,rr);
}
void stopAllMotors(){ACB_SmartCar.motorControl(1,0);ACB_SmartCar.motorControl(2,0);ACB_SmartCar.motorControl(3,0);ACB_SmartCar.motorControl(4,0);ACB_SmartCar.Move(Stop,0);}

void obstacleUpdate(bool force=false){
  if(!force&&millis()-usLastMeasureMs<150)return;
  usLastMeasureMs=millis();
  digitalWrite(PIN_ULTRA_TRIG,LOW);delayMicroseconds(2);
  digitalWrite(PIN_ULTRA_TRIG,HIGH);delayMicroseconds(10);
  digitalWrite(PIN_ULTRA_TRIG,LOW);
  long d=pulseIn(PIN_ULTRA_ECHO,HIGH,15000);
  long cm=d==0?999:d/58;if(d!=0&&cm<2)return;
  if(cm<=OBSTACLE_STOP_CM){usObsCount++;usClrCount=0;}
  else if(cm>=OBSTACLE_CLEAR_CM){usClrCount++;usObsCount=0;}
  if(usObsCount>=OBSTACLE_CONFIRM_COUNT)usObstacleHold=true;
  if(usClrCount>=OBSTACLE_CLEAR_COUNT)usObstacleHold=false;
}

int hitCount=0;
bool readSensors(long &wSum, long &tVal){
  qtr.read(sensorValues);wSum=0;tVal=0;hitCount=0;
  for(int i=0;i<SensorCount;i++){int v=sensorValues[i]-whiteBase[i];if(v<0)v=0;if(v>1200)v=1200;lineValue[i]=v;wSum+=(long)v*weight[i];tVal+=v;if(v>=80)hitCount++;}
  return tVal>=MIN_LINE_SUM;
}

void startTurn(TurnMode tm,bool forReturn=false){turnMode=tm;turnStartMs=millis();turnForReturn=forReturn;}
bool processTurn(){
  if(turnMode==TM_NONE)return false;
  unsigned long ms=turnForReturn?RETURN_BRANCH_TURN_MS:TURN_TRACK_MS;
  if(turnMode==TM_LEFT)setFourMotor(TURN_LEFT_INNER,TURN_LEFT_INNER,TURN_LEFT_OUTER,TURN_LEFT_OUTER);
  else setFourMotor(TURN_RIGHT_OUTER,TURN_RIGHT_OUTER,TURN_RIGHT_INNER,TURN_RIGHT_INNER);
  if(millis()-turnStartMs>=ms){turnMode=TM_NONE;return false;}
  delay(6);return true;
}
void resetPid(){pidLastError=0;pidFilteredError=0;hardTurnLock=false;}

int scanRFID(){
  if(state==ST_FOLLOW&&tag2Found)return 0;
  if(millis()<rfidPauseUntil)return 0;
  if(millis()-lastRfidTime<RFID_COOLDOWN)return 0;
  SPI.begin(18,19,23,5);mfrc.PCD_Init();mfrc.PCD_AntennaOn();
  if(!mfrc.PICC_IsNewCardPresent()){ACB_SmartCar.Init();SPI.begin(18,19,23,5);return 0;}
  if(!mfrc.PICC_ReadCardSerial()){ACB_SmartCar.Init();SPI.begin(18,19,23,5);return 0;}
  String uid="";
  for(byte i=0;i<mfrc.uid.size;i++){if(mfrc.uid.uidByte[i]<0x10)uid+="0";uid+=String(mfrc.uid.uidByte[i],HEX);if(i<mfrc.uid.size-1)uid+=":";}
  uid.toUpperCase();
  mfrc.PICC_HaltA();mfrc.PCD_StopCrypto1();
  ACB_SmartCar.Init();SPI.begin(18,19,23,5);
  unsigned long now=millis();
  if(uid==lastRfidUid&&now-lastSameUidMs<RFID_SAME_IGNORE)return 0;
  lastRfidUid=uid;lastSameUidMs=now;lastRfidTime=now;
  int id=0;byte*u=mfrc.uid.uidByte;
  if(mfrc.uid.size>=4){if(memcmp(u,TAG_1,4)==0)id=1;else if(memcmp(u,TAG_2,4)==0)id=2;else if(memcmp(u,TAG_START,4)==0)id=7;}
  // 모든 UID MQTT 발행
  if(mqtt.connected()){char tb[100];snprintf(tb,sizeof(tb),"{\"agv_id\":\"AGV_2\",\"rfid_uid\":\"%s\"}",uid.c_str());mqtt.publish("visipick/agv/2/rfid",tb);}
  if(id>0){
    Serial.printf("[RFID] TAG_%d\n",id);
    if(!(state==ST_GOING_START&&id!=7))rfidPauseUntil=millis()+2000;
  }
  return id;
}

void trackingMode(){
  obstacleUpdate();
  if(usObstacleHold){stopAllMotors();while(usObstacleHold){obstacleUpdate(true);delay(30);yield();}usObsCount=0;usClrCount=0;return;}
  if(processTurn())return;
  int tagId=0;
  if(state==ST_FOLLOW||state==ST_RETURN||state==ST_GOING_START) tagId=scanRFID();
  long wSum,tVal;bool lineOk=readSensors(wSum,tVal);

  // 창고 테이프
  if(state==ST_FOLLOW&&warehouseStopArmed&&lineOk&&millis()-warehouseStopArmedAt>=WH_TAPE_IGNORE_MS&&hitCount>=WH_TAPE_HIT_COUNT&&tVal>=WH_TAPE_TOTAL_MIN){
    stopAllMotors();warehouseStopArmed=false;publishStatus(mission==M_WH1?"ARRIVED_WH1":"ARRIVED_WH2");whStopStart=millis();state=ST_WH_STOP;return;}

  // 홈 테이프
  if(state==ST_HOME_APPROACH&&homeStopArmed&&lineOk&&millis()-homeStopArmedAt>=HOME_TAPE_IGNORE_MS&&hitCount>=HOME_TAPE_HIT_COUNT&&tVal>=HOME_TAPE_TOTAL_MIN){
    stopAllMotors();homeStopArmed=false;
    if(targetHome==1)home1Busy=true;else if(targetHome==2)home2Busy=true;else if(targetHome==3)home3Busy=true;
    char b[32];snprintf(b,sizeof(b),"HOME_%d_DONE",targetHome);publishStatus(b);

    lastParkedHome=targetHome;mission=M_NONE;pendingMission=M_NONE;targetHome=0;state=ST_IDLE;return;}

    // ★ 출발점 테이프 감지 (백업)
  if(state==ST_GOING_START&&lineOk&&millis()-goingStartAt>=9500
     &&hitCount>=7&&tVal>=2000){
    stopAllMotors();
    if(mqtt.connected())mqtt.publish("visipick/agv/2/rfid","{\"agv_id\":\"AGV_2\",\"rfid_uid\":\"D6:B9:39:F4\"}");
    if(exitFromHome==1)home1Busy=false;else if(exitFromHome==2)home2Busy=false;else if(exitFromHome==3)home3Busy=false;
    exitFromHome=0;state=ST_IDLE;publishStatus("AT_START");return;
  }

  // 복귀 갈림길
  if(state==ST_RETURN&&lineOk&&millis()-returnTrackStartMs>=RETURN_BRANCH_IGNORE_MS){
    int leftEdge=lineValue[0]+lineValue[1]+lineValue[2];int rightEdge=lineValue[5]+lineValue[6]+lineValue[7];
    if(returnFromWh==1&&leftEdge>=RETURN_BRANCH_THRESHOLD){startTurn(TM_LEFT,true);returnFromWh=0;return;}
    if(returnFromWh==2&&rightEdge>=RETURN_BRANCH_THRESHOLD){startTurn(TM_RIGHT,true);returnFromWh=0;return;}}

  // 홈 탈출 테이프
  if(state==ST_HOME_EXIT&&homeExitTapeArmed&&lineOk&&millis()-homeExitTapeAt>=800&&hitCount>=5&&tVal>=800){
    homeExitTapeArmed=false;
    if(exitFromHome==1)startTurn(TM_RIGHT);else if(exitFromHome==3)startTurn(TM_LEFT);
    resetPid();rfidPauseUntil=millis()+0;tag2Found=false;goingStartAt=millis();state=ST_GOING_START;publishStatus("GOING_START");return;}

  if(tagId>0)handleTag(tagId);
  if(hardTurnLock&&millis()>hardTurnIgnoreUntil&&abs(pidFilteredError)<500)hardTurnLock=false;

  if(!lineOk){
    if(pidLastError<0)setFourMotor(-150,-150,220,220);else setFourMotor(220,220,-150,-150);delay(8);return;
  }
  int error=wSum/tVal;
  pidFilteredError=(pidFilteredError*70+error*30)/100;
  // ST_GOING_START에서 강제 회전 비활성화
  if(!hardTurnLock&&error<-1800&&state!=ST_GOING_START){setFourMotor(-150,-150,220,220);pidLastError=-2000;pidFilteredError=-2000;delay(8);return;}
  if(!hardTurnLock&&error>1800&&state!=ST_GOING_START){setFourMotor(220,220,-150,-150);pidLastError=2000;pidFilteredError=2000;delay(8);return;}

  int derivative=pidFilteredError-pidLastError;pidLastError=pidFilteredError;
  int correction=(int)(0.035*pidFilteredError+0.09*derivative);correction=constrain(correction,-70,70);
  if(PID_REVERSE)correction=-correction;
  int runSpeed=baseSpeed;if(abs(pidFilteredError)>1000)runSpeed=170;else if(abs(pidFilteredError)>500)runSpeed=200;
  setFourMotor(constrain(runSpeed-correction,MIN_MOTOR_SPEED,MAX_MOTOR_SPEED),constrain(runSpeed-correction,MIN_MOTOR_SPEED,MAX_MOTOR_SPEED),
               constrain(runSpeed+correction,MIN_MOTOR_SPEED,MAX_MOTOR_SPEED),constrain(runSpeed+correction,MIN_MOTOR_SPEED,MAX_MOTOR_SPEED));
  delay(4);
}

// ── WiFi/MQTT ──
void setupWiFi(){WiFi.mode(WIFI_STA);WiFi.begin(WIFI_SSID,WIFI_PASSWORD);
  int r=0;while(WiFi.status()!=WL_CONNECTED&&r<30){delay(500);r++;}
  if(WiFi.status()==WL_CONNECTED)Serial.printf("[WiFi] Connected IP=%s\n",WiFi.localIP().toString().c_str());
  else Serial.println("[WiFi] FAIL");
}
void connectMQTT(){
  int r=0;while(!mqtt.connected()&&r<3){
    Serial.println("[MQTT] Connecting...");
    if(mqtt.connect(MQTT_CLIENT_ID)){mqtt.subscribe(MQTT_TOPIC_CMD);Serial.println("[MQTT] Connected!");}
    else{Serial.printf("[MQTT] Failed rc=%d\n",mqtt.state());delay(2000);}
    r++;
  }
}

int chooseHome(){if(!home1Busy)return 1;if(!home2Busy)return 2;if(!home3Busy)return 3;return 1;}

void startMission(Mission m){
  pendingMission=m;
  publishStatus(m==M_WH1?"PENDING_WH1":"PENDING_WH2");
}
void launchMission(){
  if(pendingMission==M_NONE){publishStatus("NO_MISSION");return;}
  mission=pendingMission;pendingMission=M_NONE;
  targetHome=0;state=ST_FOLLOW;resetPid();turnMode=TM_NONE;
  returnFromWh=0;rfidPauseUntil=0;
  warehouseStopArmed=false;homeStopArmed=false;tag2Found=false;
  publishStatus(mission==M_WH1?"MISSION_WH1":"MISSION_WH2");
}

void executeCommand(String cmd){
  cmd.trim();
  if(cmd=="GO_WAREHOUSE_1")startMission(M_WH1);
  else if(cmd=="GO_WAREHOUSE_2")startMission(M_WH2);
  else if(cmd=="TRAYS_READY_3"){uTurnStartMs=millis();state=ST_UTURN;homeExitMode=false;publishStatus("TRAYS_READY");}
  else if(cmd=="LEAVE_HOME1_TO_START"){exitFromHome=1;homeExitMode=true;uTurnStartMs=millis();state=ST_UTURN;tag2Found=false;rfidPauseUntil=millis()+10000;publishStatus("LEAVE_HOME1");}
  else if(cmd=="LEAVE_HOME2_TO_START"){exitFromHome=2;homeExitMode=true;uTurnStartMs=millis();state=ST_UTURN;tag2Found=false;rfidPauseUntil=millis()+10000;publishStatus("LEAVE_HOME2");}
  else if(cmd=="LEAVE_HOME3_TO_START"){exitFromHome=3;homeExitMode=true;uTurnStartMs=millis();state=ST_UTURN;tag2Found=false;rfidPauseUntil=millis()+10000;publishStatus("LEAVE_HOME3");}
  else if(cmd=="STOP"){stopAllMotors();state=ST_IDLE;mission=M_NONE;pendingMission=M_NONE;turnMode=TM_NONE;warehouseStopArmed=false;homeStopArmed=false;tag2Found=false;publishStatus("STOPPED");}
  else if(cmd=="EMERGENCY_STOP"){stopAllMotors();state=ST_EMERGENCY;publishStatus("EMERGENCY");}
  else if(cmd=="EMERGENCY_CLEAR"){if(state==ST_EMERGENCY){state=ST_IDLE;mission=M_NONE;publishStatus("CLEARED");}}
  else if(cmd=="HOME1_FREE")home1Busy=false;
  else if(cmd=="HOME1_BUSY")home1Busy=true;
  else if(cmd=="HOME2_FREE")home2Busy=false;
  else if(cmd=="HOME2_BUSY")home2Busy=true;
  else if(cmd=="HOME3_FREE")home3Busy=false;
  else if(cmd=="HOME3_BUSY")home3Busy=true;
  else if(cmd=="HOME_ALL_FREE"){home1Busy=false;home2Busy=false;home3Busy=false;}
  else if(cmd=="GO_START"){
    if(lastParkedHome<1||lastParkedHome>3){publishStatus("NO_HOME");return;}
    exitFromHome=lastParkedHome;homeExitMode=true;
    uTurnStartMs=millis();state=ST_UTURN;tag2Found=false;rfidPauseUntil=millis()+10000;publishStatus("GO_START");}
  else if(cmd=="TEST_LINE"){state=ST_FOLLOW;resetPid();tag2Found=true;warehouseStopArmed=false;publishStatus("TEST_LINE");}
  else if(cmd=="TEST_RETURN1"){state=ST_RETURN;returnFromWh=1;resetPid();returnTrackStartMs=millis();rfidPauseUntil=millis()+15000;publishStatus("TEST_RET1");}
  else if(cmd=="TEST_RETURN2"){state=ST_RETURN;returnFromWh=2;resetPid();returnTrackStartMs=millis();rfidPauseUntil=millis()+15000;publishStatus("TEST_RET2");}
  else if(cmd=="TEST_UTURN"){uTurnStartMs=millis();state=ST_UTURN;returnFromWh=1;mission=M_WH1;publishStatus("TEST_UTURN");}
}

void mqttCallback(char*t,byte*p,unsigned int l){char m[128];int n=min((int)l,127);memcpy(m,p,n);m[n]='\0';executeCommand(String(m));}
void handleSerial(){if(!Serial.available())return;String c=Serial.readStringUntil('\n');c.trim();executeCommand(c);}

void handleTag(int tagId){
  if(state==ST_FOLLOW&&tagId==2){
    if(mission==M_WH1){tag2Found=true;publishStatus("BRANCH_LEFT");startTurn(TM_LEFT);resetPid();warehouseStopArmed=true;warehouseStopArmedAt=millis();}
    else if(mission==M_WH2){tag2Found=true;publishStatus("BRANCH_RIGHT");startTurn(TM_RIGHT);resetPid();warehouseStopArmed=true;warehouseStopArmedAt=millis();}
    return;
  }
  // ★ 출발점 RFID 감지 (즉시 정지)
  if(state==ST_GOING_START&&tagId==7){
    stopAllMotors();
    if(exitFromHome==1)home1Busy=false;else if(exitFromHome==2)home2Busy=false;else if(exitFromHome==3)home3Busy=false;
    exitFromHome=0;state=ST_IDLE;publishStatus("AT_START");return;
  }
  if(state==ST_RETURN&&tagId==1){
    targetHome=chooseHome();publishStatus("HOME_AREA");
    int act=HOME_ACTION[targetHome];
    if(act<0){startTurn(TM_LEFT);homeStopArmed=true;homeStopArmedAt=millis();}
    else if(act>0){startTurn(TM_RIGHT);homeStopArmed=true;homeStopArmedAt=millis();}
    else{homeStopArmed=true;homeStopArmedAt=millis();}
    resetPid();state=ST_HOME_APPROACH;return;
  }
}

void setup(){
  Serial.begin(115200);
  pinMode(PIN_ULTRA_TRIG,OUTPUT);pinMode(PIN_ULTRA_ECHO,INPUT);digitalWrite(PIN_ULTRA_TRIG,LOW);
  pinMode(RFID_SS,OUTPUT);digitalWrite(RFID_SS,HIGH);
  SPI.begin(18,19,23,5);delay(50);mfrc.PCD_Init();mfrc.PCD_AntennaOn();delay(50);
  byte fw=mfrc.PCD_ReadRegister(mfrc.VersionReg);
  Serial.printf("[RFID] Ver=0x%02X %s\n",fw,(fw==0x91||fw==0x92||fw==0x12)?"OK":"FAIL");
  qtr.setTypeRC();qtr.setSensorPins(sensorPins,SensorCount);qtr.setTimeout(1000);
  ACB_SmartCar.Init();stopAllMotors();SPI.begin(18,19,23,5);
  setupWiFi();
  mqtt.setServer(MQTT_SERVER,MQTT_PORT);mqtt.setCallback(mqttCallback);mqtt.setBufferSize(512);
  connectMQTT();
  httpServer.on("/wh1",[](){startMission(M_WH1);httpServer.send(200,"text/plain","WH1 OK");});
  httpServer.on("/wh2",[](){startMission(M_WH2);httpServer.send(200,"text/plain","WH2 OK");});
  httpServer.on("/trays",[](){executeCommand("TRAYS_READY_3");httpServer.send(200,"text/plain","TRAYS OK");});
  httpServer.on("/gostart",[](){executeCommand("GO_START");httpServer.send(200,"text/plain","GOSTART OK");});
  httpServer.on("/stop",[](){stopAllMotors();state=ST_IDLE;mission=M_NONE;pendingMission=M_NONE;turnMode=TM_NONE;httpServer.send(200,"text/plain","STOP");});
  httpServer.begin();
  Serial.printf("[HTTP] http://%s/wh1 /wh2 /trays /gostart /stop\n",WiFi.localIP().toString().c_str());
  publishStatus("IDLE");Serial.println("[AGV_2] READY");
}

void loop(){
  if(!mqtt.connected())connectMQTT();
  mqtt.loop();httpServer.handleClient();handleSerial();
  if(state==ST_EMERGENCY)return;
  switch(state){
    case ST_IDLE:break;
    case ST_FOLLOW:trackingMode();break;
    case ST_WH_STOP:
      if(millis()-whStopStart>=WH_STOP_MS){returnFromWh=(mission==M_WH1)?1:2;uTurnStartMs=millis();state=ST_UTURN;publishStatus("U_TURN");}
      break;
    case ST_UTURN:
      setFourMotor(220,220,-220,-220);
      if(millis()-uTurnStartMs>=UTURN_MS){
        stopAllMotors();delay(200);resetPid();
        if(homeExitMode){homeExitMode=false;
          if(exitFromHome==2){rfidPauseUntil=millis()+0;tag2Found=false;goingStartAt=millis();state=ST_GOING_START;publishStatus("GOING_START");}
          else{homeExitTapeArmed=true;homeExitTapeAt=millis();state=ST_HOME_EXIT;publishStatus("HOME_EXIT");}
        } else if(returnFromWh>0){returnTrackStartMs=millis();rfidPauseUntil=millis()+3000;warehouseStopArmed=false;state=ST_RETURN;publishStatus("RETURNING");}
        else{launchMission();}
      }
      break;
    case ST_RETURN:trackingMode();break;
    case ST_HOME_EXIT:trackingMode();break;
    case ST_GOING_START:trackingMode();break;
    case ST_HOME_APPROACH:trackingMode();break;
    case ST_EMERGENCY:break;
  }
  if(millis()-lastStatusMs>=2000){publishHeartbeat();lastStatusMs=millis();}
}