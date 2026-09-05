# Salmon Run

탑뷰 방식으로 아래에서 위를 향해 거슬러 올라가는 2D 연어 러너 프로토타입입니다.

## 실행

1. Unity 6000.5.10f1에서 프로젝트를 엽니다.
2. `Assets/Scenes/SampleScene.unity`를 엽니다.
3. Play 버튼을 누릅니다. 별도의 씬 설정이나 프리팹 배치는 필요하지 않습니다.

## 조작

- 이동: `WASD` 또는 방향키
- 점프: `Space`
- 점프 중에는 해초, 나뭇가지, 통나무, 돌, 부유물, 어두운 물웅덩이를 넘을 수 있습니다.

## 게임 흐름

- Stage 1: 바다 / 아침 — 쉬운 장애물과 주기적인 파도
- Stage 2: 강 하류·상류 / 노을 — 강한 급류와 길을 막는 장애물
- Stage 3: 밤의 강 — 시야 방해, 빠른 장애물, 추적 피라냐
- Stage 3 완료 후 밤의 강을 반복하며 이동 속도와 함정 빈도가 계속 증가합니다.

로비의 설정 버튼에서 전체 음량을 조절할 수 있으며, 게임 종료 화면에서 즉시 다시 시작하거나 로비로 돌아갈 수 있습니다.

## 코드 위치

- `Assets/Scripts/SalmonRun/SalmonGame.cs`: 전체 게임 루프, UI, 스테이지, 점수와 체력
- `Assets/Scripts/SalmonRun/SalmonHazard.cs`: 장애물 동작
- `Assets/Scripts/SalmonRun/SalmonVisuals.cs`: 런타임 2D 그래픽 생성
- `Assets/Scripts/SalmonRun/SalmonGameBootstrap.cs`: 현재 씬에서 게임 자동 시작
