using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class Player : LivingEntity
{
    private float moveSpeed = 0.5f;
    private float airMoveSpeed = 0.3f;
    private float attackDistance = 0.3f;
    private float idenefyEntityToAttackDistance = 0.7f;
    private IslandsSpawnManager islandsSpawnManager;
    private PlayerInputActions inputActions;
    private PlayerInfo playerInfo;
    private CameraFollow cameraFollow;
    private VersatileTool versatileTool;
    private float moveDirectionX;
    private bool needToJump;
    private bool needToBend;
    private bool needToJumpToAnotherIsland;
    private ActionsInput actionsInput;
    private bool isOnJump;
    private bool isAttackingEntity;

    private float jumpDuration = 0.8f;
    private float jumpHeight = 0.4f;
    private float jumpDistance = 0.5f;

    private float jumpToAnotherIslandDuration = 0.8f;
    private float jumpToAnotherIslandHeight = 0.4f;
    private float jumpToAnotherIslandDistance = 0.32f;
    private float JumpToAnotherIslandSideMovement = 4f;

    private float lastAttackTime;
    private float attackCooldown = 0.4f;

    private NetworkVariable<bool> enableControl;

    private float positionLerpTime = 20f;
    private float rotationLerpTime = 20f;

    private NetworkVariable<PosAndFrame> realPosition;
    private NetworkVariable<Quaternion> realRotation;

    private float updateWithoutPhysicsThreshold = 0.1f;
    private float returnBackPhysicsThreshold = 0.005f;

    private bool isGrounded = true;

    private List<PlayerInputCommand> inputCommands = new List<PlayerInputCommand>();
    private List<PlayerInputCommand> recordedInputCommands = new List<PlayerInputCommand>();
    private List<PosAndFrame> recordedPosAndFrames = new List<PosAndFrame>();

    private const float inputSendInterval = 0.03f;
    private float lastInputSendTime;

    private float allowedDiscrepancyThreshold = 0.2f;

    private short currntFrame;
    private short maxFrameNum = 20000; // After reaching this number the currntFrame set to 0 
    private int inputBatchSize = 20;


    public struct PlayerInputCommand : INetworkSerializable
    {
        public short Frame;
        public ushort InputFlags; // Changed from byte to ushort

        public void SetMoveLeft(bool value) => SetFlag(0, value);
        public void SetMoveRight(bool value) => SetFlag(1, value);
        public void SetJump(bool value) => SetFlag(2, value);
        public void SetBend(bool value) => SetFlag(3, value);
        public void SetJumpToAnotherIsland(bool value) => SetFlag(4, value);
        public void SetAttack(bool value) => SetFlag(5, value);
        public void SetCut(bool value) => SetFlag(6, value);
        public void SetExtractRoots(bool value) => SetFlag(7, value);
        public void SetMine(bool value) => SetFlag(8, value); // Now works as intended

        private void SetFlag(int bit, bool value)
        {
            if (value) InputFlags |= (ushort)(1 << bit);
            else InputFlags &= (ushort)~(1 << bit);
        }

        public bool MoveLeft => (InputFlags & (1 << 0)) != 0;
        public bool MoveRight => (InputFlags & (1 << 1)) != 0;
        public bool Jump => (InputFlags & (1 << 2)) != 0;
        public bool Bend => (InputFlags & (1 << 3)) != 0;
        public bool JumpToAnotherIsland => (InputFlags & (1 << 4)) != 0;
        public bool Attack => (InputFlags & (1 << 5)) != 0;
        public bool Cut => (InputFlags & (1 << 6)) != 0;
        public bool ExtractRoots => (InputFlags & (1 << 7)) != 0;
        public bool Mine => (InputFlags & (1 << 8)) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Frame);
            serializer.SerializeValue(ref InputFlags);
        }
    }

    public struct PosAndFrame : INetworkSerializable
    {
        public short Frame;
        public Vector3 Position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Frame);
            serializer.SerializeValue(ref Position);
        }
    }

    protected override void Awake()
    {
        base.Awake();

        InitializeNetworkVariables();

        islandsSpawnManager = FindAnyObjectByType<IslandsSpawnManager>();
        cameraFollow = FindAnyObjectByType<CameraFollow>();
        versatileTool = GetComponentInChildren<VersatileTool>(true);

        inputActions = new PlayerInputActions();
        inputActions.Player.SideMove.performed += ctx => moveDirectionX = ctx.ReadValue<float>();
        inputActions.Player.SideMove.canceled += ctx => moveDirectionX = 0;
        inputActions.Player.Jump.performed += ctx => needToJump = true;
        inputActions.Player.Bend.performed += ctx => needToBend = true;
        inputActions.Player.Bend.canceled += ctx => needToBend = false;

    }

    protected override void Update()
    {
        base.Update();

        SetCurrentFrame();
        SetIsGrounded();
        RemoveOldInputBatch();

        if (enableControl.Value && IsOwner)
        {
            AdjustInputBatchSizeBasedOnLatency();
            CollectAndBatchInput(); // Batch and send inputs
        }
        else if (enableControl.Value && !IsServer)
        {
            InterpolateTransform(); // Handle interpolation for non-owner clients
        }
    }

    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        if (IsServer)
        {
            FixZPositionServer();
        }
        
        Rotate();
    }

    private void FixZPositionServer()
    {
        if (playerInfo != null)
        {
            transform.position = new Vector3(transform.position.x, transform.position.y, playerInfo.CurrentIsland.transform.position.z);
        }
    }

    public void EnablePlayer()
    {
        inputActions.Player.Enable();
        transform.SetParent(null);
        if (IsServer)
        {
            playerInfo = NextravelNetwork.Singleton.PlayersInfo.GetPlayerInfoByClientId(OwnerClientId);
            SetRawMaterialsChilrenOfThePlayer();
            enableControl.Value = true;
        }
    }

    private void RemoveOldInputBatch()
    {
        if (recordedPosAndFrames.Count > inputBatchSize)
        {
            recordedPosAndFrames.Remove(recordedPosAndFrames.First());
            recordedInputCommands.Remove(recordedInputCommands.First());
        }
    }

    private void AdjustInputBatchSizeBasedOnLatency()
    {
        double currentLatency = NextravelNetwork.Singleton.GetNetworkLatency();

        if (currentLatency < 0.1) // Less than 100 ms
        {
            inputBatchSize = 10;
        }
        else if (currentLatency < 0.2) // Between 100 ms and 200 ms
        {
            inputBatchSize = 15; 
        }
        else // Latency higher than 200 ms
        {
            inputBatchSize = 20;
        }
    }


    /// <summary>
    /// Set raw materials as chilren of the Player instead the island
    /// </summary>
    private void SetRawMaterialsChilrenOfThePlayer()
    {
        RawMaterial[] rawMaterials = GetComponent<HandleRawMaterials>().RawMaterialsInTheObject.ToArray();
        foreach (RawMaterial rawMaterial in rawMaterials)
        {
            rawMaterial.transform.SetParent(transform);
        }
    }

    public void RequestMoveRight()
    {
        moveDirectionX = 1;
    }

    public void RequestMoveLeft()
    {
        moveDirectionX = -1;
    }

    public void RequestJump()
    {
        needToJump = true;
    }

    public void RequestBend()
    {
        needToBend = true;
    }

    private void InitializeNetworkVariables()
    {
        enableControl = new NetworkVariable<bool>();
        realPosition = new NetworkVariable<PosAndFrame>();
        realRotation = new NetworkVariable<Quaternion>();

        realPosition.OnValueChanged += ReconcileState;
    }

    private void CollectAndBatchInput()
    {
        PlayerInputCommand inputCommand = GetCurrentInputCommand();

        ProcessMovement(inputCommand); // Apply inputs for prediction
        ProcessActions(inputCommand);

        // Check if it's time to send the batch
        if (Time.time - lastInputSendTime >= inputSendInterval)
        {
            if (inputCommands.Count > 0)
            {
                SubmitBatchedInputsServerRpc(inputCommands.ToArray());
                inputCommands.Clear();
            }
            lastInputSendTime = Time.time;
        }

        // Add current frame's input to the batch
        inputCommands.Add(inputCommand);
        recordedInputCommands.Add(inputCommand);
        // Record current frame's position
        recordedPosAndFrames.Add(new PosAndFrame { Position = transform.position, Frame = GetCurrentFrame() });

        actionsInput = ActionsInput.Idle;
        needToJump = false;
        needToBend = false;
        moveDirectionX = default;
    }

    [ServerRpc]
    private void SubmitBatchedInputsServerRpc(PlayerInputCommand[] inputs, ServerRpcParams rpcParams = default)
    {
        foreach (var inputCommand in inputs)
        {
            ProcessMovement(inputCommand);
            ProcessActions(inputCommand);
        }
        realPosition.Value = new PosAndFrame { Frame = inputs.Last().Frame, Position = transform.position };
        realRotation.Value = transform.rotation;
    }

    private PlayerInputCommand GetCurrentInputCommand()
    {
        PlayerInputCommand inputCommand = new PlayerInputCommand();

        inputCommand.SetMoveLeft(moveDirectionX < 0);
        inputCommand.SetMoveRight(moveDirectionX > 0);
        inputCommand.SetJump(needToJump);
        inputCommand.SetBend(needToBend);
        inputCommand.SetJumpToAnotherIsland(needToJumpToAnotherIsland);
        inputCommand.SetAttack(actionsInput.Equals(ActionsInput.Attack));
        inputCommand.SetCut(actionsInput.Equals(ActionsInput.Cut));
        inputCommand.SetExtractRoots(actionsInput.Equals(ActionsInput.ExtractRoots));
        inputCommand.SetMine(actionsInput.Equals(ActionsInput.Mine));

        inputCommand.Frame = GetCurrentFrame();

        return inputCommand;
    }

    private short GetCurrentFrame()
    {
        return currntFrame;
    }

    private void SetCurrentFrame()
    {
        currntFrame++;
        if (currntFrame >= maxFrameNum)
        {
            currntFrame = 0;
        }
    }

    private void SetIsGrounded()
    {
        Island island = islandsSpawnManager.ClientCurrentIsland;
        if (island != null)
        {
            Vector3 closestPoint = Physics.ClosestPoint(transform.position, island.GetComponentInChildren<Collider>(), island.GetComponentInChildren<Collider>().transform.position, island.GetComponentInChildren<Collider>().transform.rotation);
            float distance = Vector3.Distance(transform.position, closestPoint);
            if (distance < 0.13f)
            {
                isGrounded = true;
            }
            else
            {
                isGrounded = false;
            }
        }
    }

    private void ProcessMovement(PlayerInputCommand inputCommand)
    {
        float moveDir = inputCommand.MoveRight ? 1 : inputCommand.MoveLeft ? -1 : 0;
        bool shouldMoveX = moveDir != 0;
        if (inputCommand.Jump && !isOnJump)
        {
            StartCoroutine(JumpCoroutine(moveDir));
        }
        else if (isGrounded)
        {
            MoveState = MoveState.Idle;
        }

        if (shouldMoveX && MoveState != MoveState.Jump)
        {
            PlayerMove(moveDir);
        }

        if (inputCommand.Bend && MoveState != MoveState.Jump && MoveState != MoveState.JumpToAnotherIsland)
        {
            PlayerBend();
        }


        if (inputCommand.JumpToAnotherIsland)
        {
            PlayerJumpToAnotherIsland();
            needToJumpToAnotherIsland = false;
        }
        
        if (shouldMoveX && isAttackingEntity == false)
        {
            TargetRotation = Quaternion.Euler(0, 90 * moveDir, 0);
        }
        needToJump = false;
        SetAnimation();
    }

    private void ProcessActions(PlayerInputCommand inputCommand)
    {
        if (inputCommand.Attack)
        {
            PlayerAttack();
        }
        else if (inputCommand.Cut)
        {
            PlayerCut();
        }
        else if (inputCommand.ExtractRoots)
        {
            PlayerExtractRoots();
        }
        else if (inputCommand.Mine)
        {
            PlayerMine();
        }
        else
        {
            ActionState = ActionState.Idle;
            isAttackingEntity = false;
        }
    }

    private void InterpolateTransform()
    {
        Vector3 realPos = realPosition.Value.Position;

        float distance = Vector3.Distance(transform.position, realPos);

        if (distance > updateWithoutPhysicsThreshold) // Update without physics in oreder to close the gap no matter what
        {
            rb.isKinematic = true;
        }
        else if (distance < returnBackPhysicsThreshold)
        {
            rb.isKinematic = false;
        }

        // Interpolate other clients' transforms for smooth movement
        transform.position = Vector3.Lerp(transform.position, realPos, positionLerpTime * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, realRotation.Value, rotationLerpTime * Time.deltaTime);
    }

    private void ReconcileState(PosAndFrame oldRelPos, PosAndFrame newRelPos)
    {
        if (!IsServer)
        {
            foreach (PosAndFrame posAndFrame in recordedPosAndFrames)
            {
                if (posAndFrame.Frame == newRelPos.Frame)
                {
                    float distance = Vector3.Distance(posAndFrame.Position, newRelPos.Position);
                    if (distance > allowedDiscrepancyThreshold)
                    {
                        transform.position = newRelPos.Position;
                        ReapplyInputsFromFrame(newRelPos.Frame);
                    }
                }
            }
        }
    }

    // Reapply input predictions from a specific frame
    private void ReapplyInputsFromFrame(int frame)
    {
        foreach (PlayerInputCommand playerInputCommand in recordedInputCommands)
        {
            if (playerInputCommand.Frame > frame)
            {
                ProcessMovement(playerInputCommand);
            }
        }
    }

    private void PlayerMove(float directionX)
    {
        Vector3 newPosition = transform.position + new Vector3(directionX, 0, 0) * moveSpeed * Time.deltaTime;
        transform.position = newPosition;
        MoveState = MoveState.Walk;
    }

    private IEnumerator JumpCoroutine(float directionX)
    {
        isOnJump = true;
        rb.isKinematic = true; // Temporarily disable physics

        float time = 0;
        Vector3 lastFramePosition = Vector3.zero;
        while (time < jumpDuration)
        {
            float height = Mathf.Sin(Mathf.PI * time / jumpDuration) * jumpHeight;
            float distance = directionX * jumpDistance * (time / jumpDuration);
            Vector3 currentPositionAlongParabola = new Vector3(distance, height, 0);

            Vector3 movementSinceLastFrame = currentPositionAlongParabola - lastFramePosition;

            transform.position += movementSinceLastFrame;

            lastFramePosition = currentPositionAlongParabola;

            time += Time.deltaTime;
            yield return null;
        }
        rb.isKinematic = false;
        isOnJump = false;
    }

    private void PlayerBend()
    {
        MoveState = MoveState.Bend;
    }

    public void PlayerJumpToAnotherIslandRequest()
    {
        needToJumpToAnotherIsland = true;
    }

    private void PlayerJumpToAnotherIsland()
    {
        if (isGrounded)
        {
            //StartCoroutine(JumpToAnotherIslandCoroutine(JumpToAnotherIslandSideMovement));
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.GetComponentInParent<Island>() is Island island)
        {
            isGrounded = true;
            if (playerInfo != null && IsServer)
            {
                LoadNextIsland(island);
                StartFight(island);
                cameraFollow.SetCameraTarget(island.NetworkObject, OwnerClientId);
            }
        }
    }

    private void LoadNextIsland(Island island)
    {
        bool isTheCurrentIsland = island == playerInfo.CurrentIsland; // AKA its new island
        bool isPlayerInJourneyMode = playerInfo.GameMode == GameMode.Journey;
        if (IsServer && isTheCurrentIsland == false && isPlayerInJourneyMode)
        {
            islandsSpawnManager.SetNewIsland(OwnerClientId, island);
        }
    }

    private void StartFight(Island island)
    {
        bool IslandBelongsToServer = island.OwnerClientId == NetworkManager.ServerClientId;
        if (IslandBelongsToServer == false) // If island belongs to player
        {
            playerInfo.IsOnFight = true;
            NextravelNetwork.Singleton.PlayersInfo.GetPlayerInfoByClientId(island.OwnerClientId).IsOnFight = true;
        }
        else
        {
            playerInfo.IsOnFight = false;
        }
    }

    public void RequestAttack()
    {
        if (IsOwner)
        {
            actionsInput = ActionsInput.Attack;
        }
    }

    public void RequestCut()
    {
        if (IsOwner)
        {
            actionsInput = ActionsInput.Cut;
        }
    }

    public void RequestExtractRoots()
    {
        if (IsOwner)
        {
            actionsInput = ActionsInput.ExtractRoots;
        }
    }

    public void RequestMine()
    {
        if (IsOwner)
        {
            actionsInput = ActionsInput.Mine;
        }
    }

    private void PlayerAttack()
    {
        // Check if enough time has passed since the last attack
        if (Time.time - lastAttackTime >= attackCooldown)
        {
            LivingEntity livingEntityToAttack = FindEntityToAttack();
            if (livingEntityToAttack != null && livingEntityToAttack != this)
            {
                isAttackingEntity = true;
                float entityDirX = TransformCalculations.GetXDirectionIndicator(transform.position, livingEntityToAttack.transform.position);
                TargetRotation = Quaternion.Euler(0, entityDirX * 90, 0);

                if (IsServer)
                {
                    // attack
                    Debug.Log(livingEntityToAttack.name);
                    livingEntityToAttack.Health -= versatileTool.Damage;
                }

                lastAttackTime = Time.time; // Update the last attack time
            }
            else
            {
                isAttackingEntity = false;
            }
            ActionState = ActionState.Attack;
        }
    }

    private void PlayerCut()
    {
        ActionState = ActionState.Cut;
    }

    private void PlayerExtractRoots()
    {
        ActionState = ActionState.ExtractRoots;
    }

    private void PlayerMine()
    {
        ActionState = ActionState.Mine;
    }

    private LivingEntity FindEntityToAttack()
    {
        LivingEntity[] livingEntities = FindObjectsOfType<LivingEntity>();
        LivingEntity livingEntityToAttack = null;
        float lastDistance = float.MaxValue;
        foreach (LivingEntity entity in livingEntities)
        {
            if (entity == this) continue;
            float distance = Vector2.Distance(transform.position, entity.transform.position);
            if (distance < idenefyEntityToAttackDistance && distance < lastDistance)
            {
                lastDistance = distance;
                livingEntityToAttack = entity;
            }
        }
        return livingEntityToAttack;
    }

}

public enum ActionsInput
{
    Idle,
    Attack,
    Cut,
    ExtractRoots,
    Mine
}
