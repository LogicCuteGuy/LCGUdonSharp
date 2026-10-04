# ตัวอย่าง Native Network + LCG Network

> Documentation version: **0.3.7** · [English guide](README.md)

ลาก `NetworkExamples.prefab` ลง scene ที่มี VRC Scene Descriptor และพื้นสำหรับเดิน
แล้วเข้า Play Mode หรือ Build & Test ด้วยผู้เล่นสองคน
ไฟเริ่มต้นเป็น OFF; กดที่ลูกบอลเพื่อเปิด–ปิด และกด cube เพื่อขยับ
prefab มีป้ายแยกตัวอย่างและ reference ครบแล้ว

```text
NetworkExamples
├── Native lamp                  [NativeNetworkLamp + Collider + Light]
└── LCG zone                     [LCGNetworkZone + BoxCollider (Is Trigger)]
    ├── LCG lamp                 [LCGNetworkLamp + Collider + Light]
    └── Moving cube              [LCGNetworkMovingCube + VRC_ObjectSync]
```

## 1. NativeNetworkLamp — สถานะทั้ง instance

- `[UdonSynced] bool lightOn` เก็บสถานะไฟสำหรับผู้เล่นใน instance รวมคนเข้าใหม่
- ผู้กดส่ง `SendCustomNetworkEvent(NetworkEventTarget.Owner, ...)` ให้ owner เดิม
- owner ตรวจผู้ส่ง เปลี่ยนค่า แล้วเรียก `RequestSerialization()`
- ฝั่งรับใช้ `OnDeserialization()` เปิด–ปิด Light
- มี cooldown 0.25 วินาทีที่ owner; ไม่แย่ง ownership ทุกครั้งที่กด
- วางนอก LCG zone ตาม prefab เพื่อใช้ native sync โดยตรง

## 2. LCGNetworkLamp — สถานะภายใน zone

- `[LCGPacket(Authority = LCGPacketAuthority.ObjectOwner, Callback = ...)]`
  ส่งค่าฟิลด์ล่าสุดผ่าน LCG transport ให้สมาชิก zone
- ไม่ต้องเรียก `RequestSerialization()` กับฟิลด์ LCG
- ผู้กดที่ไม่ใช่ owner ส่ง packet `_RequestToggle` ไปหา owner;
  `__lcgPacketSender` คือผู้ส่งที่ transport ตรวจแล้ว ไม่ใช่ player ID จาก payload
- กดได้เฉพาะใน zone; owner และผู้ส่งต้องเป็นสมาชิก zone
- callback `OnLightChanged(VRCPlayerApi sender)` ใช้ปรับ Light ฝั่งรับ
- zone ขอ snapshot จาก owner เมื่อผู้เล่นเข้าหรือกลับเข้า zone;
  ตัวอย่างไม่ได้พึ่ง event เปิด–ปิดเพียงอย่างเดียว
- สถานะ LCG เป็นของ session ไม่ใช่ VRChat persistence
- UI Button สามารถเรียก `UdonBehaviour.SendCustomEvent` โดยใส่
  `_PingFirstOtherMember` เพื่อใช้ `SendLCGNetworkEvent(target, ...)`
  ส่งข้อความให้สมาชิกอีกคนเท่านั้น เปิด `Log Messages` เพื่อดูข้อความฝั่งรับ
- ค่า Exit Mode ของ prefab เป็น Freeze: ออกนอก zone แล้วคงภาพล่าสุดไว้

## 3. LCGNetworkMovingCube — ขยับวัตถุผ่าน motion queue

- ต้องมี `VRC_ObjectSync` บน GameObject เดียวกับสคริปต์ และเป็นลูกของ zone
- ผู้กดใน zone รับ ownership, ตรวจ `Networking.IsOwner`, ขยับ transform
  แล้วเรียก `LCGNetwork.RequestObjectSync(gameObject)`
- ตัวอย่างจำกัดการกด 4 ครั้ง/วินาที และวนตำแหน่ง X ในพื้นที่ตัวอย่าง
- ตอน Play/Build ตัวประมวลผลแทน `VRC_ObjectSync` ด้วย `LCGManualObjectSync`
  แล้วใช้ motion queue ที่รวมค่าล่าสุดและ interpolation
- ใช้ตำแหน่งธรรมดาเพื่อให้เห็น interpolation; หากต้องการ teleport แบบ snap
  ใช้ `LCGManualObjectSync.TeleportTo(position, rotation)` หลังได้ relay
- ผู้เล่นสองคนที่กด cube พร้อมกันยังอาจแย่ง ownership กันได้;
  ระบบเกมที่ต้องตัดสินผู้ชนะควรใช้ owner คนเดียวรับคำสั่งเหมือนตัวอย่างไฟ

## High bandwidth — `HighBandwidthExamples.prefab`

วางไว้ใน `TestLCGUdonSharp.unity` ใกล้ spawn ฝั่งซ้ายที่ `(-9, 0, 0)` แล้ว
ทั้งสองชุดเริ่มเป็น STOPPED; กด controller เพื่อเริ่ม/หยุด

- **NativeHighBandwidthExample** ส่ง `[UdonSynced] byte[]` แบบ Manual นอก zone
  ค่าเริ่มต้น 2048 bytes ที่ 4 Hz; ปรับ Inspector `Payload Bytes` (64–8192)
  และ `Requested Hz` (1–10) ได้ สคริปต์จำกัดค่าซ้ำตอน runtime
  owner เดิมเป็นผู้ส่ง ไม่แย่ง ownership ทุก sample
  รอ `OnPostSerialization` ก่อนแก้ buffer รอบถัดไป และพักเมื่อ `IsClogged`
  ป้าย Serialized B/s นับ `byteCount` ของ serialization ที่สำเร็จ รวม overhead;
  ไม่ใช่ความเร็วบนสายหรือการยืนยันว่าปลายทางได้รับ ฝั่งรับมี revision/จำนวน snapshot
  สถานะ native รองรับคนเข้าทีหลัง
- **LCGHighBandwidthExample** มี 32 cube; เริ่มทำงาน 16 ตัวที่ 20 Hz
  ปรับ `Active Objects` และ `Sample Hz` เพื่อเทียบ 4×10, 16×20, 32×30 ได้
  ต้องเดินเข้า trigger ก่อนกด owner รับ ownership ของ cube ตอนเริ่มครั้งเดียว
  แต่ละ sample เรียก `LCGNetwork.RequestObjectSync` ผ่าน motion relay
  ป้ายแสดง sample ที่ผลิตในเครื่อง, จำนวน cube ที่เครื่องนี้เป็น owner,
  คิว motion/จำนวน batch ที่ dispatch/ขนาด batch ล่าสุดของ **ทั้ง scene**
  ตัวนับ router อ่าน register ภายในเพื่อ debug; ไม่ใช่ API สาธารณะหรือ ACK ฝั่งรับ

ความถี่ producer สูงกว่าที่ router ส่งได้จะทดสอบการรวมค่าล่าสุด
LCG ยังใช้ budget motion เดิมประมาณ 6 KB/s, 40 dispatch/s และ batch ไม่เกิน 900 bytes
เพิ่ม Hz ไม่ได้เพิ่ม budget; budget นี้เฉพาะ motion ของทั้ง scene ไม่รวม native sync
หรือ LCG traffic ชนิดอื่น ต้องมีผู้เล่นอีกคนใน zone จึงเกิด remote motion traffic
ClientSim คนเดียวอาจเห็น cube ขยับและ sample เพิ่ม แต่คิว/batch เป็นศูนย์ได้

ทดสอบหลาย client โดยเปิดทีละชุด เทียบ revision/การเคลื่อนที่ ฝั่งรับ
ลองหยุด ออก–กลับเข้า zone เข้า instance ทีหลัง และให้ owner ออก
ตรวจ snapshot และคิวไม่โตไม่สิ้นสุด; ยังไม่ได้อ้างผล bandwidth/FPS ของ VRChat หลาย client
ClientSim คนเดียวใน project นี้ไม่เรียก callback serialization ให้ native behaviour ทั่วไป
จึงเห็น Pending เป็น True หลังขอส่งครั้งแรก ให้ใช้ VRChat Build & Test ตรวจ native stream/B/s
การฉีด callback ใน editor ใช้ตรวจ handler เท่านั้น ไม่ใช่หลักฐานความเร็วส่งจริง

## ตั้งค่าเอง

1. แต่ละ `.cs` ต้องมี `UdonSharpProgramAsset` `.asset` ชื่อเดียวกัน
   ตัวอย่างใน package มีคู่ asset ให้แล้ว
2. Native lamp: เพิ่ม Collider, `NativeNetworkLamp` แล้วลาก Light ใส่ช่อง Lamp
3. Zone: เพิ่ม BoxCollider เปิด Is Trigger แล้วเพิ่ม `LCGNetworkZone`
4. LCG lamp/cube: สร้างเป็นลูกของ zone, เพิ่ม Collider และสคริปต์
   ลาก zone ใส่ช่อง Zone; lamp ต้องมี Light และ cube ต้องมี `VRC_ObjectSync`
5. อย่าย้าย native lamp ลง zone โดยตรง: native `[UdonSynced]` ถูกปฏิเสธโดยค่าเริ่มต้น
   หากเปิด Allow Native Sync Passthrough ค่า native ยังส่งทั้ง instance
6. Compile UdonSharp และ Build world ใหม่หลังเปลี่ยน network runtime

## ตรวจด้วยผู้เล่นสองคน

เมื่อเข้า zone และหลัง `OnPlayerRestored` ระบบขอ snapshot ของ fields และ object state ปัจจุบันอีกครั้ง มี retry เพิ่มได้ 5 ครั้งและหยุดเมื่อออก zone หาก owner ออกจาก instance แล้ว VRChat ส่งเจ้าของให้คนนอก zone callback จะส่งต่อให้สมาชิกใน zone ที่อยู่มานานที่สุด เมื่อ zone ว่างจะคง fallback owner ของ VRChat จนมีสมาชิกเข้ามาใหม่ ต้อง Build world ใหม่เพื่อใช้การแก้ไขนี้

1. กด native lamp: ทั้งสองคนต้องเห็นค่าเดียวกัน แม้อีกคนอยู่นอก zone
2. เข้า zone ทั้งคู่ แล้วสลับกันกด LCG lamp: ไฟต้องตรงกันโดยไม่เปลี่ยน owner ทุกครั้ง
3. ให้คนหนึ่งออก zone แล้วอีกคนกด LCG lamp: คนนอกคงค่าล่าสุด;
   กลับเข้า zone ต้องได้สถานะปัจจุบันจาก snapshot
4. ให้ owner ออก zone/ออก instance: ผู้เล่นที่เหลือต้องกดไฟและขยับ cube ได้
5. กด cube: คนใน zone เห็นการเคลื่อนที่; คนนอก zone ไม่ได้รับ motion ต่อเนื่อง
6. ให้คนใหม่เข้าทีหลัง: native sync และ snapshot ของ zone ต้องแสดงค่าปัจจุบัน

Play Mode ใช้ตรวจ component/compile/local interaction ได้;
การส่งข้ามเครื่อง, late join และ ownership แข่งขันกันต้องตรวจใน VRChat หลาย client
